// Granfeldt MVEngine
// Version History
// September 1, 2012 | Soren Granfeldt
//  - rewritten for C#; based on old framework for ILM 2007 written in 2009
// September 11, 2012 | Soren Granfeldt
//  - optimized logging and RuleApply functions
//  - added initial flow logic for AttributeFlowConstantWithMVValueReplace
//  - added regex replacement for SourceExpressionConstant and AttributeFlowConstant
// September 24, 2012 | Soren Granfeldt
//  - rename element InitialsFlow to InitialFlows (removed 's' from Initials...)
// November 29, 2012 | Soren Granfeldt
//  - added option to disable all rules / provisioning
// December 4, 2012 | Soren Granfeldt
//  - added ConditionNotMatch condition
// February 14, 2013 | Soren Granfeldt
//  - added ConditionConnectedTo and ConditionNotConnectedTo conditions
// December 18, 2013 | Soren Granfeldt
//  - added conditional-rename rule
//  - added new conditions (IsPresent and IsNotPresent)
//  - added suggestions from Niels Rossen
//      - convert binary to string using BitConverter and additional error handling around existing connectors
// september 15, 2014 | soren granfeldt
//  - added options for escaping DN components in initial flows (AttributeFlowConstant) on provision and rename (documentation pending)
//  - optimized rule selection and filtering for added performance (using Linq namespace)
//  - optimized some logging text
// february 19, 2015 | soren granfeldt
//  -added loading of seperate rules files
//  -removing disabled rules right after load for faster processing with many rules
// February 24, 2015 | Ryan Newington
//  - added support for specifying additional object classes in provisioning rules

using Microsoft.MetadirectoryServices;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace Granfeldt
{
    public class MVEngine : IMVSynchronization
    {
        static string DebugLogFilename = null;
        private RulesFile EngineRules = new RulesFile();

        #region Logging

        static public Mutex mutex = new Mutex();

        enum EntryPointAction
        {
            Enter,
            Leave
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern void OutputDebugString(string message);

        static void Log(string s)
        {
            s = GetEntryPointName() + " " + s;
            mutex.WaitOne();
            if (!string.IsNullOrEmpty(DebugLogFilename))
            {
                using (StreamWriter f = new StreamWriter(DebugLogFilename, true))
                {
                    f.WriteLine(s);
                }
            }
            mutex.ReleaseMutex();
            OutputDebugString(s);
        }
        static void Log(string key, object value)
        {
            Log(string.Format("{0}: {1}", key, value.ToString()));
        }
        static void Log(Exception ex)
        {
            Log(string.Format("{0}: {1}", ex.GetType().ToString(), ex.Message));
        }
        static void Log(EntryPointAction entryPointAction)
        {
            Log(string.Format("{0}: {1}", GetEntryPointName(), entryPointAction.ToString()));
        }
        public static string GetEntryPointName()
        {
            StackTrace trace = new StackTrace();
            int index = 0;
            string str = null;
            for (index = trace.FrameCount - 2; index >= 2; index += -1)
            {
                if (str != null)
                {
                    str = str + "->";
                }
                str = str + trace.GetFrame(index).GetMethod().Name;
            }
            trace = null;
            return str;
        }

        #endregion

        #region Helper Functions

        public string FIMInstallationDirectory()
        {
            return Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\FIMSynchronizationService\Parameters", false).GetValue("Path").ToString();
        }

        #endregion

        #region IMVSynchronization Methods

        public void Initialize()
        {
            try
            {
                try
                {
                    DebugLogFilename = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Granfeldt\FIM\MRE", false).GetValue("DebugLogFilename", null).ToString();
                    DebugLogFilename = string.Format(DebugLogFilename, DateTime.Now);
                }
                catch (System.NullReferenceException ex)
                {
                    ex.ToString();
                    // we get here if key og hive doesn't exist which is perfectly okay
                }

                Log(EntryPointAction.Enter);
                Log("Loading rules");
                // loading default rules file
                new MVRules().LoadSettingsFromFile(Path.Combine(Utils.ExtensionsDirectory, "fim.mre.xml"), ref this.EngineRules);
                string[] ruleFiles = Directory.GetFiles(Utils.ExtensionsDirectory, "*.fim.mre.xml", SearchOption.TopDirectoryOnly);
                foreach (string ruleFile in ruleFiles)
                {
                    RulesFile rules = new RulesFile();
                    Log("loading-rule-file", ruleFile);
                    new MVRules().LoadSettingsFromFile(ruleFile, ref rules);
                    foreach (Rule rule in rules.Rules)
                    {
                        EngineRules.Rules.Add(rule);
                    }
                }

                foreach (Rule rule in EngineRules.Rules)
                {
                    if (rule.Enabled)
                    {
                        Log("active-rule", rule.Name);
                    }
                    else
                    {
                        Log("inactive-rule", rule.Name);
                    }
                }
                Log("removing-inactive-rules", EngineRules.Rules.Count(ru => !ru.Enabled));
                EngineRules.Rules.RemoveAll(rule => !rule.Enabled);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }
        public void Provision(MVEntry mventry)
        {
            if (EngineRules.DisableAllRules)
            {
                Log("Provisioning is disabled");
                return;
            }
            try
            {
                Log(EntryPointAction.Enter);
                foreach (Rule rule in EngineRules.Rules.Where(mv => mv.Enabled && mv.SourceObject.Equals(mventry.ObjectType, StringComparison.OrdinalIgnoreCase)))
                {
                    Log(string.Format("Start rule '{0}' (MA {1})", rule.Name, rule.TargetManagementAgentName));
                    Log(string.Format("Object ({0}): {1} (GUID {2})", mventry.ObjectType, mventry["displayName"].IsPresent ? mventry["displayName"].Value : "", mventry.ObjectID));
                    CSEntry csentry = null;
                    ConnectedMA ma = mventry.ConnectedMAs[rule.TargetManagementAgentName];
                    switch (rule.Action.ToLower())
                    {
                        case "rename":
                            if (ma.Connectors.Count == 1 && ConditionsApply(csentry, mventry, rule.ConditionalRename.Conditions))
                            {
                                csentry = ma.Connectors.ByIndex[0];
                                this.RenameConnector(ma, csentry, mventry, rule);
                                this.ConditionalRenameConnector(ma, csentry, mventry, rule);
                            }
                            break;
                        case "provision":
                            if (ma.Connectors.Count == 0)
                            {
                                if (ConditionsApply(csentry, mventry, rule.Conditions))
                                {
                                    this.CreateConnector(ma, csentry, mventry, rule);
                                }
                            }
                            else
                            {
                                if (ma.Connectors.Count == 1)
                                {
                                    csentry = ma.Connectors.ByIndex[0];
                                    this.RenameConnector(ma, csentry, mventry, rule);
                                    this.ConditionalRenameConnector(ma, csentry, mventry, rule);
                                }
                                else
                                {
                                    Log(new Exception("More than one connector (" + ma.Connectors.Count + ") exists"));
                                }
                            }
                            break;
                        case "deprovision":
                            if (ma.Connectors.Count == 1 && ConditionsApply(csentry, mventry, rule.Conditions))
                            {
                                csentry = ma.Connectors.ByIndex[0];
                                this.DeprovisionConnector(ma, csentry, mventry, rule);
                            }
                            break;
                        default:
                            Log(new Exception("Invalid action specified"));
                            break;
                    }
                    Log("End rule", rule.Name);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }
        public bool ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {
            throw new EntryPointNotImplementedException();
        }
        public void Terminate()
        {
            try
            {
                Log(EntryPointAction.Enter);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }

        #endregion

        public void CreateConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            try
            {
                Log(EntryPointAction.Enter);
                csentry = ma.Connectors.StartNewConnector(connectorRule.TargetObject);
                this.SetupInitialValues(ma, csentry, mventry, connectorRule);
                csentry.CommitNewConnector();
                this.AddAdditionalObjectClasses(csentry, mventry, connectorRule);
            }
            catch (ObjectAlreadyExistsException ex)
            {
                Log(ex);
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }

        private void AddAdditionalObjectClasses(CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            if (!string.IsNullOrEmpty(connectorRule.TargetObjectAdditionalClasses))
            {
                ValueCollection newObjectClasses = Utils.ValueCollection(csentry.ObjectType);

                Match match = Regex.Match(connectorRule.TargetObjectAdditionalClasses, @"^#mv\:(?<attrname>\w+)#$", RegexOptions.Compiled);
                string[] valuesToAdd = null;

                if (match.Success)
                {
                    string attributeName = match.Groups["attrname"].Value;
                    valuesToAdd = mventry[attributeName].IsPresent ? mventry[attributeName].Values.ToStringArray() : new string[0];
                }
                else
                {
                    valuesToAdd = connectorRule.TargetObjectAdditionalClasses.Split(',');
                }

                if (valuesToAdd != null)
                {
                    foreach (string value in valuesToAdd)
                    {
                        newObjectClasses.Add(value);
                    }
                }

                if (newObjectClasses.Count > 1)
                {
                    csentry.ObjectClass = newObjectClasses;
                }
            }
        }

        public void DeprovisionConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            try
            {
                Log(EntryPointAction.Enter);
                csentry.Deprovision();
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }
        public void ConditionalRenameConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            try
            {
                Log(EntryPointAction.Enter);
                if (connectorRule.ConditionalRename == null)
                    return;

                string escapedCN = null;
                string replacedValue = null;
                if (string.IsNullOrEmpty(connectorRule.ConditionalRename.EscapedCN))
                {
                    Log("No CN to escape");
                    replacedValue = ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.NewDNValue, mventry);
                }
                else
                {
                    escapedCN = ma.EscapeDNComponent(ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.EscapedCN, mventry, "")).ToString();
                    Log("EscapedCN", escapedCN);
                    replacedValue = ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.NewDNValue, mventry, escapedCN);
                }
                ReferenceValue newdn = ma.CreateDN(replacedValue);
                Log("Old DN", csentry.DN.ToString());
                Log("New DN", newdn.ToString());
                if (csentry.DN.ToString().Equals(newdn.ToString()))
                {
                    Log("No renaming necessary");
                }
                else
                {
                    csentry.DN = ma.CreateDN(replacedValue);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }
        public void RenameConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            try
            {
                Log(EntryPointAction.Enter);
                if (connectorRule.RenameDnFlow == null)
                    return;

                if (((connectorRule.RenameDnFlow != null) & connectorRule.RenameDnFlow.SourceValueIsPresent()) & connectorRule.RenameDnFlow.TargetValueIsPresent())
                {
                    if (mventry[connectorRule.RenameDnFlow.Source].IsPresent && !string.IsNullOrEmpty(mventry[connectorRule.RenameDnFlow.Source].Value))
                    {
                        if (connectorRule.RenameDnFlow.ReprovisionOnRename)
                        {
                            if (mventry[connectorRule.RenameDnFlow.Source].Value != csentry[connectorRule.RenameDnFlow.Target].Value)
                            {
                                Log("Performing reprovisioning " + csentry.DN.ToString());
                                Log("Source(mv): " + mventry[connectorRule.RenameDnFlow.Source].Value);
                                Log("Target(cs): " + mventry[connectorRule.RenameDnFlow.Source].Value);
                                DeprovisionConnector(ma, csentry, mventry, connectorRule);
                                CreateConnector(ma, csentry, mventry, connectorRule);
                            }
                            else
                            {
                                Log("No difference in source and target; no reprovisioning will be done " + csentry.DN.ToString());
                            }
                        }
                        else
                        {
                            if (connectorRule.RenameDnFlow.Target.ToUpper() == "[DN]")
                            {
                                Log("Current CS:DN is '" + csentry.DN.ToString() + "'");
                                Log("Renaming to MV::" + connectorRule.RenameDnFlow.Source + ", value: '" + mventry[connectorRule.RenameDnFlow.Source].Value + "'");
                                if (mventry[connectorRule.RenameDnFlow.Source].IsPresent)
                                {
                                    csentry.DN = ma.CreateDN(mventry[connectorRule.RenameDnFlow.Source].Value);
                                }
                                else
                                {
                                    Log(new Exception(connectorRule.RenameDnFlow.Source + " is not present in metaverse", new Exception("")));
                                }
                            }
                            else
                            {
                                mventry[connectorRule.RenameDnFlow.Source].Value = mventry[connectorRule.RenameDnFlow.Source].Value;
                            }
                        }
                    }
                    else
                    {
                        Log(new Exception(connectorRule.RenameDnFlow.Source + " is null, empty or not present in metaverse", new Exception("")));
                    }
                }
                else
                {
                    Log("No source and/or target for renaming is specified, hence no DN renaming is done");
                }
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }
        public bool ConditionsApply(CSEntry csentry, MVEntry mventry, Conditions conditions)
        {
            try
            {
                Log(EntryPointAction.Enter);
                if (conditions == null || conditions.ConditionBase.Count == 0)
                {
                    Log("Applies (no conditions specified)");
                    return true;
                }
                foreach (ConditionBase conditionBase in conditions.ConditionBase)
                {
                    if (conditionBase.GetType() == typeof(ConditionIsPresent))
                    {
                        ConditionIsPresent condition = (ConditionIsPresent)conditionBase;
                        if (!mventry[condition.MVAttribute].IsPresent)
                        {
                            Log("Condition failed (Reason: Metaverse attribute value is present)", conditionBase.Description);
                            return false;
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionIsNotPresent))
                    {
                        ConditionIsNotPresent condition = (ConditionIsNotPresent)conditionBase;
                        if (mventry[condition.MVAttribute].IsPresent)
                        {
                            Log("Condition failed (Reason: Metaverse attribute value is present)", conditionBase.Description);
                            return false;
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionMatch))
                    {
                        ConditionMatch condition = (ConditionMatch)conditionBase;
                        if (!mventry[condition.MVAttribute].IsPresent)
                        {
                            Log("Condition failed (Reason: No metaverse value is present)", conditionBase.Description);
                            return false;
                        }
                        else
                        {
                            if (!Regex.IsMatch(mventry[condition.MVAttribute].Value, condition.Pattern, RegexOptions.IgnoreCase))
                            {
                                Log("Condition failed (Reason: RegEx doesnt match)", conditionBase.Description);
                                return false;
                            }
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionNotMatch))
                    {
                        ConditionNotMatch condition = (ConditionNotMatch)conditionBase;
                        if (mventry[condition.MVAttribute].IsPresent)
                        {
                            if (Regex.IsMatch(mventry[condition.MVAttribute].Value, condition.Pattern, RegexOptions.IgnoreCase))
                            {
                                Log("Condition failed (Reason: RegEx match)", conditionBase.Description);
                                return false;
                            }
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionAttributeIsNotPresent))
                    {
                        ConditionAttributeIsNotPresent condition = (ConditionAttributeIsNotPresent)conditionBase;
                        if (mventry[condition.MVAttribute].IsPresent)
                        {
                            Log("Condition failed (Reason: Metaverse attribute is present)", conditionBase.Description);
                            return false;
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionAttributeIsPresent))
                    {
                        ConditionAttributeIsPresent condition = (ConditionAttributeIsPresent)conditionBase;
                        if (!mventry[condition.MVAttribute].IsPresent)
                        {
                            Log("Condition failed (Reason: Metaverse attribute is not present)", conditionBase.Description);
                            return false;
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionNotConnectedTo))
                    {
                        ConditionNotConnectedTo condition = (ConditionNotConnectedTo)conditionBase;
                        ConnectedMA MA = mventry.ConnectedMAs[condition.ManagementAgentName];
                        if (MA.Connectors.Count > 0)
                        {
                            Log(string.Format("Condition failed (Reason: Still connected to {0})", condition.ManagementAgentName), conditionBase.Description);
                            return false;
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionConnectedTo))
                    {
                        ConditionConnectedTo condition = (ConditionConnectedTo)conditionBase;
                        ConnectedMA MA = mventry.ConnectedMAs[condition.ManagementAgentName];
                        if (MA.Connectors.Count < 1)
                        {
                            Log(string.Format("Condition failed (Reason: Not connected to {0})", condition.ManagementAgentName), conditionBase.Description);
                            return false;
                        }
                    }
                }

                // if we get here, all conditions have been met
                Log("Applies (all conditions are met)");
                return true;
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }
        public string ReplaceWithMVValueOrBlank(string source, MVEntry mventry)
        {
            return ReplaceWithMVValueOrBlank(source, mventry, "");
        }
        public string ReplaceWithMVValueOrBlank(string source, MVEntry mventry, string escapedCN)
        {
            source = Regex.Replace(source, @"#param:EscapedCN#", escapedCN ?? "", RegexOptions.IgnoreCase);

            MatchCollection mc = Regex.Matches(source, @"(?<=#mv\:)(?<attrname>\w+)#", RegexOptions.Compiled);
            foreach (Match match in mc)
            {
                string newValue = mventry[match.Value.Trim('#')].IsPresent ? mventry[match.Value.Trim('#')].Value : "";
                Log("Match", newValue);
                source = Regex.Replace(source, string.Format(@"#mv\:{0}", match.Value), mventry[match.Value.Trim('#')].Value);
            }
            return source;
        }
        public void SetupInitialValues(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            try
            {
                Log(EntryPointAction.Enter);
                foreach (AttributeFlowBase attributeBase in connectorRule.InitialFlows)
                {
                    #region Guid
                    if (attributeBase.GetType() == typeof(AttributeFlowGuid))
                    {
                        AttributeFlowGuid attribute = (AttributeFlowGuid)attributeBase;
                        Guid newGuid = Guid.NewGuid();
                        Log("\tNew GUID '" + newGuid.ToString() + "' to CS '" + attribute.Target + "'");

                        if (attribute.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                            csentry.DN = csentry.MA.CreateDN(newGuid.ToString());
                        else
                            csentry[attribute.Target].Value = newGuid.ToString();
                        continue;
                    }
                    #endregion

                    #region Attribute
                    if (attributeBase.GetType() == typeof(AttributeFlowAttribute))
                    {
                        AttributeFlowAttribute attrFlow = (AttributeFlowAttribute)attributeBase;
                        string TargetValue;
                        if (mventry[attrFlow.Source].DataType == AttributeType.Binary)
                        {
                            TargetValue = BitConverter.ToString(mventry[attrFlow.Source].BinaryValue);
                            TargetValue = TargetValue.Replace("-", "");
                        }
                        else
                        {
                            TargetValue = mventry[attrFlow.Source].Value;
                        }

                        if (attrFlow.LowercaseTargetValue) { TargetValue = TargetValue.ToLower(); }
                        if (attrFlow.UppercaseTargetValue) { TargetValue = TargetValue.ToUpper(); }
                        if (attrFlow.TrimTargetValue) { TargetValue = TargetValue.Trim(); }

                        TargetValue = (string.IsNullOrEmpty(attrFlow.Prefix)) ? TargetValue : attrFlow.Prefix + TargetValue;

                        Log("\tFlow value from MV '" + attrFlow.Source + "' to '" + attrFlow.Target + "'");
                        Log("\tSource value: '" + mventry[attrFlow.Source].Value + "'");
                        Log("\tTarget value: '" + TargetValue + "'");
                        if (attrFlow.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                            csentry.DN = csentry.MA.CreateDN(TargetValue);
                        else
                            csentry[attrFlow.Target].Value = TargetValue;
                        continue;
                    }
                    #endregion

                    #region Constant
                    if (attributeBase.GetType() == typeof(AttributeFlowConstant))
                    {
                        AttributeFlowConstant attrFlow = (AttributeFlowConstant)attributeBase;

                        string escapedCN = null;
                        string replacedValue = null;
                        if (string.IsNullOrEmpty(attrFlow.EscapedCN))
                        {
                            Log("\tNo CN to escape");
                            replacedValue = ReplaceWithMVValueOrBlank(attrFlow.Constant, mventry);
                        }
                        else
                        {
                            escapedCN = ma.EscapeDNComponent(ReplaceWithMVValueOrBlank(attrFlow.EscapedCN, mventry, "")).ToString();
                            Log("\tEscapedCN", escapedCN);
                            replacedValue = ReplaceWithMVValueOrBlank(attrFlow.Constant, mventry, escapedCN);
                        }
                        Log("\tFlow constant '" + replacedValue + "' to '" + attrFlow.Target + "'");
                        if (attrFlow.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                            csentry.DN = csentry.MA.CreateDN(replacedValue);
                        else
                            csentry[(attrFlow.Target)].Value = replacedValue;
                        continue;
                    }
                    #endregion

                    #region  Concatenate
                    if (attributeBase.GetType() == typeof(AttributeFlowConcatenate))
                    {
                        Log("Building concatenated value");
                        AttributeFlowConcatenate attrFlow = (AttributeFlowConcatenate)attributeBase;
                        string concatValue = null;
                        foreach (SourceExpressionBase sourceExpression in attrFlow.SourceExpressions)
                        {
                            if (sourceExpression.GetType() == typeof(SourceExpressionConstant))
                            {
                                SourceExpressionConstant sourceExpr = (SourceExpressionConstant)sourceExpression;
                                string replacedValue = ReplaceWithMVValueOrBlank(sourceExpr.Source, mventry);
                                Log("\tAdding constant '" + replacedValue + "' to concatenated value");
                                concatValue = concatValue + replacedValue;
                                continue;
                            }
                            if (sourceExpression.GetType() == typeof(SourceExpressionRegexReplace))
                            {
                                SourceExpressionRegexReplace sourceExpr = (SourceExpressionRegexReplace)sourceExpression;
                                Log("\tAdding RegEx replacement '" + sourceExpr.Source + "' to concatenated value");
                                if (mventry[sourceExpr.Source].IsPresent)
                                {
                                    concatValue = concatValue + Regex.Replace(mventry[sourceExpr.Source].Value, sourceExpr.Pattern, sourceExpr.Replacement);
                                }
                                else
                                {
                                    Log(new Exception("Attribute '" + sourceExpr.Source + "' is not present in metaverse", new Exception("")));
                                }
                                continue;
                            }
                            if (sourceExpression.GetType() == typeof(SourceExpressionAttribute))
                            {
                                SourceExpressionAttribute attr = (SourceExpressionAttribute)sourceExpression;
                                if (mventry[attr.Source].IsPresent)
                                {
                                    Log("\tAdding value of MV::" + attr.Source + "::'" + mventry[attr.Source].Value.ToString() + "' to concatenated value");
                                    concatValue = concatValue + mventry[attr.Source].Value.ToString();
                                }
                                else
                                {
                                    Log(new Exception("Attribute '" + attr.Source + "' is not present in metaverse", new Exception("")));
                                }
                                continue;
                            }
                        }
                        Log("Flow concatenated attribute value '" + concatValue + "' to '" + attrFlow.Target + "'");
                        if (attrFlow.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                            csentry.DN = csentry.MA.CreateDN(concatValue);
                        else
                            csentry[(attrFlow.Target)].Value = concatValue;
                        continue;
                    }
                    #endregion
                }
            }
            catch (Exception ex)
            {
                Log(ex);
                throw ex;
            }
            finally
            {
                Log(EntryPointAction.Leave);
            }
        }
    }
}
