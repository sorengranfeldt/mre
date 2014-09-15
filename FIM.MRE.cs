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

using Microsoft.MetadirectoryServices;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using System.Linq;

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
                Log(EntryPointAction.Enter);
                Log("Loading rules");
                new MVRules().LoadSettingsFromFile(Path.Combine(Utils.ExtensionsDirectory, "FIM.MRE.xml"), ref this.EngineRules);

                DebugLogFilename = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Granfeldt\FIM\MRE", false).GetValue("DebugLogFilename", null).ToString();
                DebugLogFilename = string.Format(DebugLogFilename, DateTime.Now);
            }
            catch (System.NullReferenceException ex)
            {
                ex.ToString();
                // we get here if key og hive doesn't exist which is perfectly okay
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
                Log("Current DN value", csentry.DN);
                Log("New DN value", replacedValue);
                ReferenceValue newdn = ma.CreateDN(replacedValue);
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
        public bool ConditionsApply(CSEntry csentry, MVEntry mventry, ConditionBase[] conditions)
        {
            try
            {
                Log(EntryPointAction.Enter);
                if (conditions.Length == 0)
                {
                    Log("Applies (no conditions specified)");
                    return true;
                }
                foreach (ConditionBase conditionBase in conditions)
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

    #region Rule

    public class RulesFile
    {
        public bool DisableAllRules = false;
        public Rule[] Rules;
    }

    public class Rule
    {
        public string Action;
        public ConditionBase[] Conditions;
        public string Description = "";
        public bool Enabled = false;
        public AttributeFlowBase[] InitialFlows;
        public string Name = "";
        public RenameDnFlow RenameDnFlow = null;
        public RenameAction ConditionalRename = null;
        public string SourceObject;
        public string TargetManagementAgentName = "";
        public string TargetObject;
    }

    public class RenameAction
    {
        public string EscapedCN;
        /// <summary>
        /// Use #mv:??# notation
        /// </summary>
        public string NewDNValue = null;
        /// <summary>
        /// Specify metaverse attribute name or [DN]
        /// </summary>
        public string DNAttribute = null;
        /// <summary>
        /// Conditions that must be met for renaming to take place
        /// </summary>
        public ConditionBase[] Conditions;
    }

    public class RenameDnFlow
    {
        public bool ReprovisionOnRename = false;
        public string Source = null;
        public string Target = null;

        public bool OnlyRenameOnRegularExpressionMatch = false;
        public string RegExMVAttributeName = null;
        public string RegExFilter = null;

        public bool SourceValueIsPresent()
        {
            if (this.Source == null)
            {
                return false;
            }
            if (string.IsNullOrEmpty(this.Source))
            {
                return false;
            }
            return true;
        }
        public bool TargetValueIsPresent()
        {
            if (this.Target == null)
            {
                return false;
            }
            if (string.IsNullOrEmpty(this.Target))
            {
                return false;
            }
            return true;
        }
    }

    public class MVRules
    {

        #region Methods

        public void LoadSettingsFromFile(string Filename, ref RulesFile Rules)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(RulesFile));
            serializer.UnreferencedObject += new UnreferencedObjectEventHandler(this.Serializer_UnreferencedObject);
            StreamReader textReader = new StreamReader(Filename);
            Rules = (RulesFile)serializer.Deserialize(textReader);
            textReader.Close();
        }
        public void SaveRulesConfigurationFile(ref RulesFile F, string Filename)
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(RulesFile));
                StreamWriter writer = new StreamWriter(Filename, false);
                serializer.Serialize((TextWriter)writer, F);
                writer.Close();
            }
            catch (Exception exception1)
            {
                Exception exception = exception1;
                throw;
            }
        }
        private void Serializer_UnknownAttribute(object sender, XmlAttributeEventArgs e)
        {
            XmlAttribute attr = e.Attr;
            throw new Exception("Unknown attribute " + attr.Name + "='" + attr.Value + "'");
        }
        private void Serializer_UnknownNode(object sender, XmlNodeEventArgs e)
        {
            throw new Exception("Unknown Node:" + e.Name + "\t" + e.Text);
        }
        private void Serializer_UnreferencedObject(object sender, UnreferencedObjectEventArgs e)
        {
            throw new Exception("UnreferencedObject: " + e.UnreferencedObject.ToString());
        }

        #endregion

    }

    #endregion

    #region Source Expression

    [XmlInclude(typeof(SourceExpressionRegexReplace)), XmlInclude(typeof(SourceExpressionConstant)), XmlInclude(typeof(SourceExpressionAttribute))]
    public class SourceExpressionBase
    {
        public string Source;
    }

    public class SourceExpressionAttribute : SourceExpressionBase
    {
    }

    public class SourceExpressionConstant : SourceExpressionBase
    {
    }

    public class SourceExpressionRegexReplace : SourceExpressionBase
    {
        public string Pattern;
        public string Replacement;
    }

    #endregion

    #region ConditionBase

    [XmlInclude(typeof(ConditionAttributeIsPresent)), XmlInclude(typeof(ConditionMatch)), XmlInclude(typeof(ConditionNotMatch)), XmlInclude(typeof(ConditionAttributeIsNotPresent)), XmlInclude(typeof(ConditionConnectedTo)), XmlInclude(typeof(ConditionNotConnectedTo)), XmlIncludeAttribute(typeof(ConditionIsPresent)), XmlIncludeAttribute(typeof(ConditionIsNotPresent))]
    public class ConditionBase
    {
        public string Description = "";
    }

    public class ConditionIsPresent : ConditionBase
    {
        public string MVAttribute = "";
    }

    public class ConditionIsNotPresent : ConditionBase
    {
        public string MVAttribute = "";
    }

    public class ConditionMatch : ConditionBase
    {
        public string MVAttribute = "";
        public string Pattern = "";
    }

    public class ConditionNotMatch : ConditionBase
    {
        public string MVAttribute = "";
        public string Pattern = "";
    }

    public class ConditionAttributeIsPresent : ConditionBase
    {
        public string MVAttribute = "";
    }

    public class ConditionAttributeIsNotPresent : ConditionBase
    {
        public string MVAttribute = "";
    }

    public class ConditionConnectedTo : ConditionBase
    {
        public string ManagementAgentName = "";
    }

    public class ConditionNotConnectedTo : ConditionBase
    {
        public string ManagementAgentName = "";
    }

    #endregion

    #region AttributeFlow

    [XmlInclude(typeof(AttributeFlowAttribute)), XmlInclude(typeof(AttributeFlowConcatenate)), XmlInclude(typeof(AttributeFlowConstant)), XmlInclude(typeof(AttributeFlowGuid))]
    public class AttributeFlowBase
    {
        public string Description = null;
        public string Target = null;
    }

    public class AttributeFlowConcatenate : AttributeFlowBase
    {
        public SourceExpressionBase[] SourceExpressions;
    }

    public class AttributeFlowConstant : AttributeFlowBase
    {
        public string EscapedCN = null;
        public string Constant = null;
    }

    public class AttributeFlowGuid : AttributeFlowBase
    {
    }

    public class AttributeFlowAttribute : AttributeFlowBase
    {
        public string Prefix = null;
        public string Source = null;
        public bool LowercaseTargetValue = false;
        public bool UppercaseTargetValue = false;
        public bool TrimTargetValue = false;
    }

    #endregion

}
