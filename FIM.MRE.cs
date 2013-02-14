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

// CONSIDER 
//  - AN ESCAPEDN IMPLEMENTATION
//  - AN CONDITIONISTRUE/FALSE for handling isADAccount booleans

using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.MetadirectoryServices;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Threading;

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
                foreach (Rule rule in EngineRules.Rules)
                {
                    if (rule.Enabled)
                    {
                        Log("Start rule", rule.Name);
                        Log("Unique identifier (GUID)", mventry.ObjectID);
                        Log("Display Name", mventry["displayName"].IsPresent ? mventry["displayName"].Value : "");
                        Log("Object type", mventry.ObjectType);
                        if (mventry.ObjectType.Equals(rule.SourceObject, StringComparison.OrdinalIgnoreCase))
                        {
                            CSEntry csentry = null;
                            ConnectedMA ma = mventry.ConnectedMAs[rule.TargetManagementAgentName];
                            if (rule.Action.Equals("provision", StringComparison.OrdinalIgnoreCase))
                            {
                                if (ma.Connectors.Count == 0)
                                {
                                    if (this.RuleApply(ma, csentry, mventry, rule))
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
                                    }
                                    else
                                    {
                                        Log(new Exception("More than one connector (" + ma.Connectors.Count + ") exists"));
                                    }
                                }
                            }
                            else if ((rule.Action.Equals("deprovision", StringComparison.OrdinalIgnoreCase) && RuleApply(ma, csentry, mventry, rule)) && (ma.Connectors.Count == 1))
                            {
                                csentry = ma.Connectors.ByIndex[0];
                                this.DeprovisionConnector(ma, csentry, mventry, rule);
                            }
                        }
                        Log("End rule", rule.Name);
                    }
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
                this.SetupInitialValues(csentry, mventry, connectorRule);
                csentry.CommitNewConnector();
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
        public void RenameConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            try
            {
                Log(EntryPointAction.Enter);
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
        public bool RuleApply(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            try
            {
                Log(EntryPointAction.Enter);
                if (connectorRule.Conditions.Length == 0)
                {
                    Log("Rule applies (no conditions specified)", connectorRule.Name);
                    return true;
                }
                foreach (ConditionBase conditionBase in connectorRule.Conditions)
                {
                    if (conditionBase.GetType() == typeof(ConditionMatch))
                    {
                        ConditionMatch condition = (ConditionMatch)conditionBase;
                        if (!mventry[condition.MVAttribute].IsPresent)
                        {
                            Log("Rule failed (Reason: No metaverse value is present)", conditionBase.Description);
                            return false;
                        }
                        else
                        {
                            if (!Regex.IsMatch(mventry[condition.MVAttribute].Value, condition.Pattern, RegexOptions.IgnoreCase))
                            {
                                Log("Rule failed (Reason: RegEx doesnt match)", conditionBase.Description);
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
                                Log("Rule failed (Reason: RegEx match)", conditionBase.Description);
                                return false;
                            }
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionAttributeIsNotPresent))
                    {
                        ConditionAttributeIsNotPresent condition = (ConditionAttributeIsNotPresent)conditionBase;
                        if (mventry[condition.MVAttribute].IsPresent)
                        {
                            Log("Rule failed (Reason: Metaverse attribute is present)", conditionBase.Description);
                            return false;
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionAttributeIsPresent))
                    {
                        ConditionAttributeIsPresent condition = (ConditionAttributeIsPresent)conditionBase;
                        if (!mventry[condition.MVAttribute].IsPresent)
                        {
                            Log("Rule failed (Reason: Metaverse attribute is not present)", conditionBase.Description);
                            return false;
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionNotConnectedTo))
                    {
                        ConditionNotConnectedTo condition = (ConditionNotConnectedTo)conditionBase;
                        ConnectedMA MA = mventry.ConnectedMAs[condition.ManagementAgentName];
                        if (MA.Connectors.Count > 0)
                        {
                            Log(string.Format("Rule failed (Reason: Still connected to {0})", condition.ManagementAgentName), conditionBase.Description);
                            return false;
                        }
                    }

                    if (conditionBase.GetType() == typeof(ConditionConnectedTo))
                    {
                        ConditionConnectedTo condition = (ConditionConnectedTo)conditionBase;
                        ConnectedMA MA = mventry.ConnectedMAs[condition.ManagementAgentName];
                        if (MA.Connectors.Count < 1)
                        {
                            Log(string.Format("Rule failed (Reason: Not connected to {0})", condition.ManagementAgentName), conditionBase.Description);
                            return false;
                        }
                    }
                }

                // if we get here, all conditions have been met
                Log("Rule applies (all conditions are met)", connectorRule.Name);
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
            MatchCollection mc = Regex.Matches(source, @"(?<=#mv\:)(?<attrname>\w+)#", RegexOptions.Compiled);
            foreach (Match match in mc)
            {
                string newValue = mventry[match.Value.Trim('#')].IsPresent ? mventry[match.Value.Trim('#')].Value : "";
                Log("Match", newValue);
                source = Regex.Replace(source, string.Format(@"#mv\:{0}", match.Value), mventry[match.Value.Trim('#')].Value);
            }
            return source;
        }
        public void SetupInitialValues(CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            try
            {
                Log(EntryPointAction.Enter);
                foreach (AttributeFlowBase attributeBase in connectorRule.InitialFlows)
                {
                    Log("Initial Attribute Flow", attributeBase.GetType().ToString());

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
                        string TargetValue = mventry[attrFlow.Source].Value;
                        if (attrFlow.LowercaseTargetValue) { TargetValue = TargetValue.ToString().ToLower(); }
                        if (attrFlow.UppercaseTargetValue) { TargetValue = TargetValue.ToString().ToUpper(); }
                        if (attrFlow.TrimTargetValue) { TargetValue = TargetValue.ToString().Trim(); }

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
                        string replacedValue = ReplaceWithMVValueOrBlank(attrFlow.Constant, mventry);
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
        public string SourceObject;
        public string TargetManagementAgentName = "";
        public string TargetObject;
    }

    public class RenameDnFlow
    {
        public bool ReprovisionOnRename = false;
        public string Source = null;
        public string Target = null;

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

    [XmlInclude(typeof(ConditionAttributeIsPresent)), XmlInclude(typeof(ConditionMatch)), XmlInclude(typeof(ConditionNotMatch)), XmlInclude(typeof(ConditionAttributeIsNotPresent)), XmlInclude(typeof(ConditionConnectedTo)), XmlInclude(typeof(ConditionNotConnectedTo))]
    public class ConditionBase
    {
        public string Description = "";
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

#region obsolete code

//public void DumpMvEntry(MVEntry mventry)
//{
//    return;
//    try
//    {
//        this.TraceRoutine("Enter");
//        Log("MV->ObjectID: " + mventry.ObjectID.ToString());
//        Log("MV->ObjectType: " + mventry.ObjectType.ToString());
//        AttributeNameEnumerator enumerator = mventry.GetEnumerator();
//        enumerator.Reset();
//        while (enumerator.MoveNext())
//        {
//            string current = enumerator.Current;
//            if (mventry[current].IsPresent)
//            {
//                if (mventry[current].IsMultivalued)
//                {
//                    if (mventry[current].DataType == AttributeType.Reference)
//                    {
//                        Log("\tMV::" + current + " (multi-value): '(unable to show reference value)'");
//                    }
//                    else
//                    {
//                        ValueCollectionEnumerator enumerator2 = mventry[current].Values.GetEnumerator();
//                        while (enumerator2.MoveNext())
//                        {
//                            Value value2 = enumerator2.Current;
//                            Log("\tMV::" + current + " (multi-value): '" + value2.ToString() + "'");
//                        }
//                    }
//                }
//                else if (mventry[current].DataType == ((AttributeType)((int)AttributeType.Reference)))
//                {
//                    Log("\tMV::" + current + ": (unable to show reference value) [" + mventry[current].LastContributingMA.Name + " - " + (mventry[current].LastContributionTime.ToLocalTime()) + "]");
//                }
//                else
//                {
//                    Log("\tMV::" + current + ": '" + mventry[current].Value + "' [" + mventry[current].LastContributingMA.Name + " - " + (mventry[current].LastContributionTime.ToLocalTime()) + "]");
//                }
//            }
//            else
//            {
//                Log("\t\tMV::" + current + ": [NOT PRESENT]");
//            }
//        }
//        ConnectedMACollectionEnumerator enumerator3 = mventry.ConnectedMAs.GetEnumerator();
//        while (enumerator3.MoveNext())
//        {
//            ConnectedMA dma = enumerator3.Current;
//            CSEntry entry = dma.Connectors.ByIndex[0];
//            Log("\tMA->" + dma.Name);
//            Log("\t\tCS->ObjectType: " + entry.ObjectType);
//            Log("\t\tCS->DN: " + entry.DN.ToString());
//            Log("\t\tCS->RDN: " + entry.RDN.ToString());
//            Log("\t\tCS->ConnectionChangeTime: " + (entry.ConnectionChangeTime.ToLocalTime()));
//            Log("\t\tCS->ConnectionState: " + entry.ConnectionState.ToString());
//            Log("\t\tCS->ConnectionRule: " + entry.ConnectionRule.ToString());
//            CSEntry entry3 = entry;
//            enumerator = entry.GetEnumerator();
//            enumerator.Reset();
//            while (enumerator.MoveNext())
//            {
//                string str2 = enumerator.Current;
//                if (entry[str2].IsPresent)
//                {
//                    if (entry[str2].IsMultivalued)
//                    {
//                        if (entry[str2].DataType == ((AttributeType)((int)AttributeType.Reference)))
//                        {
//                            Log("\tCS::" + str2 + " (multi-value): '(unable to show reference value)'");
//                        }
//                        else
//                        {
//                            ValueCollectionEnumerator enumerator4 = entry[str2].Values.GetEnumerator();
//                            while (enumerator4.MoveNext())
//                            {
//                                Value value3 = enumerator4.Current;
//                                Log("\t\tCS::" + str2 + " (multi-value): '" + value3.ToString() + "'");
//                            }
//                        }
//                    }
//                    else
//                    {
//                        Log("\t\tCS::" + str2 + ": '" + entry[str2].Value + "'");
//                    }
//                }
//                else
//                {
//                    Log("\t\tCS::" + str2 + ": [NOT PRESENT]");
//                }
//            }
//            entry3 = null;
//        }
//    }
//    catch (Exception ex)
//    {
//        Log(ex);
//        throw ex;
//    }
//    finally
//    {
//        this.TraceRoutine("Exit");
//    }
//}


#endregion