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
//  -added loading of separate rules files
//  -removing disabled rules right after load for faster processing with many rules
// february 24, 2015 | ryan newington
//  -added support for specifying additional object classes in provisioning rules
// february 27, 2015 | soren granfeldt
//  -added tracesource and removed old logging function
// march 10, 2015 | soren granfeldt
//  -marked RenameConnector as obsolete
//  -moved condition logic to the classes

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
        protected TraceSource trace;
        const string TraceSourceName = "FIM.MRE";

        static string DebugLogFilename = null;
        private RulesFile EngineRules = new RulesFile();

        #region IMVSynchronization Methods

        public void Initialize()
        {
            try
            {
                trace = new TraceSource(TraceSourceName, SourceLevels.All);
                trace.TraceEvent(TraceEventType.Information, 0, "enter-initialize");
                try
                {
                    RegistryKey machineRegistry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);

                    DebugLogFilename = machineRegistry.OpenSubKey(@"SOFTWARE\Granfeldt\FIM\MRE", false).GetValue("DebugLogFilename", null).ToString();
                    DebugLogFilename = string.Format(DebugLogFilename, DateTime.Now);

                    TextWriterTraceListener i = new TextWriterTraceListener(Path.ChangeExtension(DebugLogFilename, ".log"), TraceSourceName);
                    i.TraceOutputOptions = TraceOptions.DateTime;
                    i.Filter = new EventTypeFilter(SourceLevels.All);
                    StreamWriter sw = i.Writer as StreamWriter;
                    if (sw != null) sw.AutoFlush = true;
                    trace.Listeners.Add(i);

                    TextWriterTraceListener el = new TextWriterTraceListener(Path.ChangeExtension(DebugLogFilename, ".errors.log"), TraceSourceName);
                    el.TraceOutputOptions = TraceOptions.DateTime | TraceOptions.Callstack;
                    el.Filter = new EventTypeFilter(SourceLevels.Critical | SourceLevels.Error | SourceLevels.Warning);
                    StreamWriter elstream = el.Writer as StreamWriter;
                    if (elstream != null) elstream.AutoFlush = true;
                    trace.Listeners.Add(el);
                }
                catch (System.NullReferenceException ex)
                {
                    // we get here if key og hive doesn't exist which is perfectly okay
                    // i have yet to find a better method for determining whether 
                    // a specific registry exists or not to avoid using this
                    // try/catch thingy
                    trace.TraceInformation("no-logging-file-specified {0}", ex.Message);
                }

                string extensionsDirectory = Utils.ExtensionsDirectory;
#if DEBUG
                extensionsDirectory = Directory.GetCurrentDirectory();
#endif
                trace.TraceInformation("loading-default-rules-file");
                new MVRules().LoadSettingsFromFile(Path.Combine(extensionsDirectory, "fim.mre.xml"), ref this.EngineRules);
                trace.TraceInformation("loading-additional-rules-files");
                string[] ruleFiles = Directory.GetFiles(extensionsDirectory, "*.fim.mre.xml", SearchOption.TopDirectoryOnly);
                foreach (string ruleFile in ruleFiles)
                {
                    RulesFile rules = new RulesFile();
                    trace.TraceInformation("loading-rule-file {0}", ruleFile);
                    new MVRules().LoadSettingsFromFile(ruleFile, ref rules);
                    foreach (Rule rule in rules.Rules)
                    {
                        EngineRules.Rules.Add(rule);
                    }
                }
                trace.TraceInformation("evaluating-{0}-rules", EngineRules.Rules.Count);
                foreach (Rule rule in EngineRules.Rules)
                {
                    if (rule.Enabled && rule.RenameDnFlow != null)
                    {
                        trace.TraceEvent(TraceEventType.Warning, 1, "RenameConnector XML is obsolete and will be removed from coming version.");
                    }

                    if (rule.Enabled)
                    {
                        trace.TraceInformation("found-active-rule {0}", rule.Name);
                    }
                    else
                    {
                        trace.TraceInformation("found-inactive-rule {0}", rule.Name);
                    }
                }
                trace.TraceInformation("removing-inactive-{0}-rules", EngineRules.Rules.Count(ru => !ru.Enabled));
                EngineRules.Rules.RemoveAll(rule => !rule.Enabled);
            }
            catch (Exception ex)
            {
                trace.TraceEvent(TraceEventType.Error, ex.HResult, ex.InnerException.Message);
                throw;
            }
            finally
            {
                trace.TraceEvent(TraceEventType.Information, 0, "exit-initialize");
            }
        }
        public void Provision(MVEntry mventry)
        {
            trace.TraceEvent(TraceEventType.Information, 0, "enter-provision");
            if (EngineRules.DisableAllRules)
            {
                trace.TraceInformation("provisioning-is-disabled");
                return;
            }
            try
            {
                foreach (Rule rule in EngineRules.Rules.Where(mv => mv.SourceObject.Equals(mventry.ObjectType, StringComparison.OrdinalIgnoreCase)))
                {
                    trace.TraceInformation("start-rule '{0}' (MA {1})", rule.Name, rule.TargetManagementAgentName);
                    trace.TraceInformation("Object ({0}): {1} (GUID {2})", mventry.ObjectType, mventry["displayName"].IsPresent ? mventry["displayName"].Value : "", mventry.ObjectID);
                    CSEntry csentry = null;
                    ConnectedMA ma = mventry.ConnectedMAs[rule.TargetManagementAgentName];
                    switch (rule.Action)
                    {
                        case RuleAction.Rename:
                            if (ma.Connectors.Count == 1 && ConditionsApply(csentry, mventry, rule.ConditionalRename.Conditions))
                            {
                                csentry = ma.Connectors.ByIndex[0];
                                this.RenameConnector(ma, csentry, mventry, rule);
                                this.ConditionalRenameConnector(ma, csentry, mventry, rule);
                            }
                            break;
                        case RuleAction.Provision:
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
                                    trace.TraceEvent(TraceEventType.Error, 1, "more-than-one-connector-(" + ma.Connectors.Count + ")-exists");
                                }
                            }
                            break;
                        case RuleAction.Deprovision:
                            if (ma.Connectors.Count == 1 && ConditionsApply(csentry, mventry, rule.Conditions))
                            {
                                csentry = ma.Connectors.ByIndex[0];
                                this.DeprovisionConnector(ma, csentry, mventry, rule);
                            }
                            break;
                        case RuleAction.DeprovisionAll:
                            if (ConditionsApply(csentry, mventry, rule.Conditions))
                            {
                                mventry.ConnectedMAs.DeprovisionAll();
                            }
                            break;
                        default:
                            trace.TraceEvent(TraceEventType.Error, 1, "invalid-action-specified");
                            break;
                    }
                    trace.TraceInformation("end-rule {0}", rule.Name);
                }
            }
            catch (Exception ex)
            {
                trace.TraceEvent(TraceEventType.Error, ex.HResult, ex.Message);
                throw;
            }
            finally
            {
                trace.TraceEvent(TraceEventType.Information, 0, "exit-provision");
            }
        }
        public bool ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {
            throw new EntryPointNotImplementedException();
        }
        public void Terminate()
        {
            trace.TraceEvent(TraceEventType.Information, 0, "terminate");
        }

        #endregion

        public void CreateConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            trace.TraceEvent(TraceEventType.Information, 0, "enter-createconnector");
            try
            {
                IList<string> additionalObjectClasses = this.GetAdditionalObjectClasses(mventry, rule);
                if (additionalObjectClasses.Count > 0)
                {
                    csentry = ma.Connectors.StartNewConnector(rule.TargetObject, additionalObjectClasses.ToArray());
                }
                else
                {
                    csentry = ma.Connectors.StartNewConnector(rule.TargetObject);
                }

                this.SetupInitialValues(ma, csentry, mventry, rule);
                csentry.CommitNewConnector();
            }
            catch (Exception ex)
            {
                trace.TraceEvent(TraceEventType.Error, ex.HResult, ex.Message);
                throw;
            }
            finally
            {
                trace.TraceEvent(TraceEventType.Information, 0, "exit-createconnector");
            }
        }
        private IList<string> GetAdditionalObjectClasses(MVEntry mventry, Rule connectorRule)
        {
            List<string> valuesToAdd = new List<string>();
            if (!string.IsNullOrEmpty(connectorRule.TargetObjectAdditionalClasses))
            {
                Match match = Regex.Match(connectorRule.TargetObjectAdditionalClasses, @"^#mv\:(?<attrname>\w+)#$", RegexOptions.Compiled);
                if (match.Success)
                {
                    string attributeName = match.Groups["attrname"].Value;
                    if (mventry[attributeName].IsPresent)
                    {
                        valuesToAdd.AddRange(mventry[attributeName].Values.ToStringArray());
                    }
                }
                else
                {
                    valuesToAdd.AddRange(connectorRule.TargetObjectAdditionalClasses.Split(','));
                }
            }

            if (valuesToAdd.Count > 0)
            {
                valuesToAdd.Insert(0, connectorRule.TargetObject);
            }

            return valuesToAdd;
        }

        public void DeprovisionConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            trace.TraceEvent(TraceEventType.Information, 0, "enter-deprovisionconnector");
            try
            {
                csentry.Deprovision();
            }
            catch (Exception ex)
            {
                trace.TraceEvent(TraceEventType.Error, ex.HResult, ex.Message);
                throw;
            }
            finally
            {
                trace.TraceEvent(TraceEventType.Information, 0, "exit-deprovisionconnector");
            }
        }
        public void ConditionalRenameConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            trace.TraceEvent(TraceEventType.Information, 0, "enter-conditionalrenameconnector");
            try
            {
                if (connectorRule.ConditionalRename == null)
                    return;

                string escapedCN = null;
                string replacedValue = null;
                if (string.IsNullOrEmpty(connectorRule.ConditionalRename.EscapedCN))
                {
                    trace.TraceInformation("no-cn-to-escape");
                    replacedValue = ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.NewDNValue, mventry);
                }
                else
                {
                    escapedCN = ma.EscapeDNComponent(ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.EscapedCN, mventry, "")).ToString();
                    trace.TraceInformation("escaped-cn {0}", escapedCN);
                    replacedValue = ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.NewDNValue, mventry, escapedCN);
                }
                ReferenceValue newdn = ma.CreateDN(replacedValue);
                trace.TraceInformation("old-dn {0}", csentry.DN.ToString());
                trace.TraceInformation("new-dn {0}", newdn.ToString());
                if (csentry.DN.ToString().Equals(newdn.ToString()))
                {
                    trace.TraceInformation("no-renaming-necessary");
                }
                else
                {
                    csentry.DN = ma.CreateDN(replacedValue);
                }
            }
            catch (Exception ex)
            {
                trace.TraceEvent(TraceEventType.Error, ex.HResult, ex.Message);
                throw;
            }
            finally
            {
                trace.TraceEvent(TraceEventType.Information, 0, "exit-conditionalrenameconnector");
            }
        }

        [Obsolete("Use ConditionalRename instead")]
        public void RenameConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            if (connectorRule.RenameDnFlow == null)
                return;

            trace.TraceEvent(TraceEventType.Information, 0, "enter-renameconnector");
            try
            {
                if (((connectorRule.RenameDnFlow != null) & connectorRule.RenameDnFlow.SourceValueIsPresent()) & connectorRule.RenameDnFlow.TargetValueIsPresent())
                {
                    if (mventry[connectorRule.RenameDnFlow.Source].IsPresent && !string.IsNullOrEmpty(mventry[connectorRule.RenameDnFlow.Source].Value))
                    {
                        if (connectorRule.RenameDnFlow.ReprovisionOnRename)
                        {
                            if (mventry[connectorRule.RenameDnFlow.Source].Value != csentry[connectorRule.RenameDnFlow.Target].Value)
                            {
                                trace.TraceInformation("Reprovisioning {0}", csentry.DN.ToString());
                                trace.TraceInformation("Source(mv): {0}", mventry[connectorRule.RenameDnFlow.Source].Value);
                                trace.TraceInformation("Target(cs): {0}", mventry[connectorRule.RenameDnFlow.Target].Value);
                                DeprovisionConnector(ma, csentry, mventry, connectorRule);
                                CreateConnector(ma, csentry, mventry, connectorRule);
                            }
                            else
                            {
                                trace.TraceInformation("No difference in source and target; no reprovisioning will be done {0}", csentry.DN.ToString());
                            }
                        }
                        else
                        {
                            if (connectorRule.RenameDnFlow.Target.ToUpper() == "[DN]")
                            {
                                trace.TraceInformation("Current CS:DN is '{0}'", csentry.DN.ToString());
                                trace.TraceInformation("Renaming to MV::{0}, value: '{1}'", connectorRule.RenameDnFlow.Source, mventry[connectorRule.RenameDnFlow.Source].Value);
                                if (mventry[connectorRule.RenameDnFlow.Source].IsPresent)
                                {
                                    csentry.DN = ma.CreateDN(mventry[connectorRule.RenameDnFlow.Source].Value);
                                }
                                else
                                {
                                    trace.TraceInformation("{0} is not present in metaverse", connectorRule.RenameDnFlow.Source);
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
                        trace.TraceInformation("{0} is null, empty or not present in metaverse", connectorRule.RenameDnFlow.Source);
                    }
                }
            }
            catch (Exception ex)
            {
                trace.TraceEvent(TraceEventType.Error, ex.HResult, ex.Message);
                throw;
            }
            finally
            {
                trace.TraceEvent(TraceEventType.Information, 0, "exit-renameconnector");
            }
        }

        public bool ConditionsApply(CSEntry csentry, MVEntry mventry, Conditions conditions)
        {
            trace.TraceEvent(TraceEventType.Information, 0, "enter-conditionsapply");
            try
            {
                if (conditions == null || conditions.ConditionBase.Count == 0)
                {
                    trace.TraceInformation("No conditions specified (returning true)");
                    return true;
                }
                return conditions.Met(mventry, csentry, trace);
            }
            catch (Exception ex)
            {
                trace.TraceEvent(TraceEventType.Error, ex.HResult, ex.Message);
                throw;
            }
            finally
            {
                trace.TraceEvent(TraceEventType.Information, 0, "exit-conditionsapply");
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
                string matchValue = match.Value.Trim('#');
                string newValue = mventry[matchValue].IsPresent ? mventry[matchValue].Value : "";
                trace.TraceInformation("replaced '{0}' with '{1}'", matchValue, newValue);
                source = Regex.Replace(source, string.Format(@"#mv\:{0}", match.Value), mventry[matchValue].Value);
            }
            return source;
        }

        public void SetupInitialValues(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            trace.TraceEvent(TraceEventType.Information, 0, "enter-setupinitialvalues");
            try
            {
                foreach (AttributeFlowBase attributeBase in connectorRule.InitialFlows)
                {
                    if (attributeBase.GetType() == typeof(AttributeFlowGuid))
                    {
                        AttributeFlowGuid attribute = (AttributeFlowGuid)attributeBase;
                        Guid newGuid = Guid.NewGuid();
                        trace.TraceInformation("New GUID {0} to {1}", newGuid.ToString(), attribute.Target);

                        if (attribute.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                            csentry.DN = csentry.MA.CreateDN(newGuid.ToString());
                        else
                            csentry[attribute.Target].Value = newGuid.ToString();
                        continue;
                    }

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

                        trace.TraceInformation("Flow source value: '" + mventry[attrFlow.Source].Value + "'");
                        trace.TraceInformation("Target value: '" + TargetValue + "'");
                        if (attrFlow.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                            csentry.DN = csentry.MA.CreateDN(TargetValue);
                        else
                            csentry[attrFlow.Target].Value = TargetValue;
                        continue;
                    }

                    if (attributeBase.GetType() == typeof(AttributeFlowConstant))
                    {
                        AttributeFlowConstant attrFlow = (AttributeFlowConstant)attributeBase;

                        string escapedCN = null;
                        string replacedValue = null;
                        if (string.IsNullOrEmpty(attrFlow.EscapedCN))
                        {
                            trace.TraceInformation("No CN to escape");
                            replacedValue = ReplaceWithMVValueOrBlank(attrFlow.Constant, mventry);
                        }
                        else
                        {
                            escapedCN = ma.EscapeDNComponent(ReplaceWithMVValueOrBlank(attrFlow.EscapedCN, mventry, "")).ToString();
                            trace.TraceInformation("EscapedCN", escapedCN);
                            replacedValue = ReplaceWithMVValueOrBlank(attrFlow.Constant, mventry, escapedCN);
                        }
                        trace.TraceInformation("Flow constant '{0}' to {1}", replacedValue, attrFlow.Target);
                        if (attrFlow.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                            csentry.DN = csentry.MA.CreateDN(replacedValue);
                        else
                            csentry[(attrFlow.Target)].Value = replacedValue;
                        continue;
                    }

                    if (attributeBase.GetType() == typeof(AttributeFlowConcatenate))
                    {
                        trace.TraceInformation("Building concatenated value");
                        AttributeFlowConcatenate attrFlow = (AttributeFlowConcatenate)attributeBase;
                        string concatValue = null;
                        foreach (SourceExpressionBase sourceExpression in attrFlow.SourceExpressions)
                        {
                            if (sourceExpression.GetType() == typeof(SourceExpressionConstant))
                            {
                                SourceExpressionConstant sourceExpr = (SourceExpressionConstant)sourceExpression;
                                string replacedValue = ReplaceWithMVValueOrBlank(sourceExpr.Source, mventry);
                                trace.TraceInformation("Adding constant '{0}'", replacedValue);
                                concatValue = concatValue + replacedValue;
                                continue;
                            }
                            if (sourceExpression.GetType() == typeof(SourceExpressionRegexReplace))
                            {
                                SourceExpressionRegexReplace sourceExpr = (SourceExpressionRegexReplace)sourceExpression;
                                trace.TraceInformation("Adding RegEx replacement '{0}'", sourceExpr.Source);
                                if (mventry[sourceExpr.Source].IsPresent)
                                {
                                    concatValue = concatValue + Regex.Replace(mventry[sourceExpr.Source].Value, sourceExpr.Pattern, sourceExpr.Replacement);
                                }
                                else
                                {
                                    trace.TraceEvent(TraceEventType.Error, 1, "Attribute '" + sourceExpr.Source + "' is not present in metaverse");
                                }
                                continue;
                            }
                            if (sourceExpression.GetType() == typeof(SourceExpressionAttribute))
                            {
                                SourceExpressionAttribute attr = (SourceExpressionAttribute)sourceExpression;
                                if (mventry[attr.Source].IsPresent)
                                {
                                    trace.TraceInformation("Adding value of MV::{0} '{1}'", attr.Source, mventry[attr.Source].Value);
                                    concatValue = concatValue + mventry[attr.Source].Value.ToString();
                                }
                                else
                                {
                                    trace.TraceEvent(TraceEventType.Error, 1, "Attribute '" + attr.Source + "' is not present in metaverse");
                                }
                                continue;
                            }
                        }
                        trace.TraceInformation("Flow concatenated attribute value '{0}' to {1}", concatValue, attrFlow.Target);
                        if (attrFlow.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                            csentry.DN = csentry.MA.CreateDN(concatValue);
                        else
                            csentry[(attrFlow.Target)].Value = concatValue;
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                trace.TraceEvent(TraceEventType.Error, ex.HResult, ex.Message);
                throw;
            }
            finally
            {
                trace.TraceEvent(TraceEventType.Information, 0, "exit-setupinitialvalues");
            }
        }
    }
}
