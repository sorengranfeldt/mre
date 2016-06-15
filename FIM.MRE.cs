// granfeldt mvengine
// version history
// september 1, 2012 | soren granfeldt
//  - rewritten for C#; based on old framework for ILM 2007 written in 2009
// september 11, 2012 | soren granfeldt
//  - optimized logging and RuleApply functions
//  - added initial flow logic for AttributeFlowConstantWithMVValueReplace
//  - added regex replacement for SourceExpressionConstant and AttributeFlowConstant
// september 24, 2012 | soren granfeldt
//  - rename element InitialsFlow to InitialFlows (removed 's' from Initials...)
// november 29, 2012 | soren granfeldt
//  - added option to disable all rules / provisioning
// december 4, 2012 | soren granfeldt
//  - added ConditionNotMatch condition
// february 14, 2013 | soren granfeldt
//  - added ConditionConnectedTo and ConditionNotConnectedTo conditions
// december 18, 2013 | soren granfeldt
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
//  -marked RenameDnFlow as obsolete
//  -moved condition logic to the classes
// march 11, 2015 | ryan newington
//  - updated registry key logic
// april 4, 2015 | soren granfeldt
//	-added Trace logging to all functions
//	-added logging to eventlog for errors and warning
// may 1, 2015 | soren granfeldt
//	-moved all initial flow logic to the classes and simplified SetupInitialValues
//	-removed old Rename code and moved reprovision code to ConditionalRename
// september 1, 2015 | ryan newington | 1.0.7.0
//  - modified the DN comparison logic in rename connector to support comparing DNs as DNs rather than strings
//  - added support for forcing the old style of DN string-based comparison
//  - disabled obsolete warnings within the MRE code itself
// october 12, 2015 | soren granfeldt | 1.0.8.0
//	- removed support for disabling all rules
//	- added helper class for calculating initial flow values, like reusing guid and such. to be extended in later versions

namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using Microsoft.Win32;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    public class MVEngine : IMVSynchronization
    {
        const string EventLogSource = "FIM Metaverse Rules Extension";
        const string EventLogName = "Application";
        const string TraceSourceName = "FIM.MRE";
        string ruleFilesPath = Utils.ExtensionsDirectory;

        private Dictionary<string, List<Rule>> EngineRules;

        #region IMVSynchronization Methods

        public void Initialize()
        {
            Tracer.IndentLevel = 0;
            Tracer.TraceInformation("enter-initialize");
            Tracer.Indent();

            try
            {
                System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                Tracer.TraceInformation("fim-mre-version {0}", fvi.FileVersion);

                RegistryKey machineRegistry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                RegistryKey mreRootKey = machineRegistry.OpenSubKey(@"SOFTWARE\Granfeldt\FIM\MRE", false);

                if (mreRootKey != null)
                {
                    string logFileValue = mreRootKey.GetValue("DebugLogFileName", null) as string;
                    if (logFileValue != null)
                    {
                        Tracer.TraceWarning("DebugLogFileName registry key is deprecated. Use trace logging instead.");
                    }

                    string ruleFilesAlternativePath = mreRootKey.GetValue("RuleFilesAlternativePath", null) as string;
                    if (!string.IsNullOrEmpty(ruleFilesAlternativePath))
                    {
                        ruleFilesPath = ruleFilesAlternativePath;
                        Tracer.TraceInformation("registry-alternative-rulefiles-path '{0}'", ruleFilesPath);
                    }
                }

                if (!EventLog.SourceExists(EventLogSource))
                {
                    Tracer.TraceInformation("creating-eventlog-source source: {0}, logname: ", EventLogSource, EventLogName);
                    EventLog.CreateEventSource(EventLogSource, EventLogName);
                }
                EventLog evl = new EventLog(EventLogName);
                evl.Log = EventLogName;
                evl.Source = EventLogSource;

                EventLogTraceListener eventLog = new EventLogTraceListener(EventLogSource);
                eventLog.EventLog = evl;
                EventTypeFilter filter = new EventTypeFilter(SourceLevels.Warning | SourceLevels.Error | SourceLevels.Critical);
                eventLog.TraceOutputOptions = TraceOptions.Callstack;
                eventLog.Filter = filter;
                Tracer.Trace.Listeners.Add(eventLog);

#if DEBUG
                // for debugging, we use current path
                if (ruleFilesPath == Utils.ExtensionsDirectory)
                    ruleFilesPath = Directory.GetCurrentDirectory();
#endif
                Tracer.TraceInformation("loading-rule-files-from '{0}'", ruleFilesPath);
                this.EngineRules = new Dictionary<string, List<Rule>>();

                string[] ruleFiles = Directory.GetFiles(ruleFilesPath, "*fim.mre.xml", SearchOption.AllDirectories);
                foreach (string ruleFile in ruleFiles)
                {
                    RulesFile rules = new RulesFile();
                    Tracer.TraceInformation("loading-rule-file '{0}'", ruleFile);
                    MVRules.LoadSettingsFromFile(ruleFile, ref rules);

                    foreach (Rule rule in rules.Rules)
                    {
                        if (rule.Enabled)
                        {
                            Tracer.TraceInformation("found-active-rule {0}", rule.Name);
                        }
                        else
                        {
                            Tracer.TraceInformation("found-inactive-rule {0}", rule.Name);
                            continue;
                        }

                        rule.EnsureBackwardsCompatibility();

                        if (rule.Enabled && rule.RenameDnFlow != null)
                        {
                            Tracer.TraceWarning("RenameDnFlow XML element is obsolete and will not be supported in coming versions. Please see documentation for more information.");
                        }

                        if (!this.EngineRules.ContainsKey(rule.SourceObject))
                        {
                            this.EngineRules.Add(rule.SourceObject, new List<Rule>());
                        }

                        this.EngineRules[rule.SourceObject].Add(rule);
                    }
                }

                if (this.EngineRules.Count == 0)
                    Tracer.TraceWarning("no-rules-loaded");
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-initialize");
            }
        }
        public void Provision(MVEntry mventry)
        {
            Tracer.IndentLevel = 0;
            Tracer.TraceInformation("enter-provision");
            Tracer.Indent();

            if (!this.EngineRules.ContainsKey(mventry.ObjectType))
            {
                return;
            }

            try
            {
                HashSet<string> executedMAs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Rule rule in this.EngineRules[mventry.ObjectType])
                {
                    if (executedMAs.Contains(rule.TargetManagementAgentName))
                    {
                        // skip rule as we have already executed something for this MA and connector
                        continue;
                    }

                    Tracer.TraceInformation("start-rule '{0}' (MA {1})", rule.Name, rule.TargetManagementAgentName);
                    Tracer.TraceInformation("{0}, displayname: {1}, mvguid: {2}", mventry.ObjectType, mventry["displayName"].IsPresent ? mventry["displayName"].Value : "n/a", mventry.ObjectID);
                    ConnectedMA ma = mventry.ConnectedMAs[rule.TargetManagementAgentName];

                    if (ma.Connectors.Count == 0)
                    {
                        // If we don't have any connectors, the only rule that can 
                        // apply is a provisioning rule

                        if (rule.Action == RuleAction.Provision)
                        {
                            if (this.ConditionsApply(null, mventry, rule.Conditions))
                            {
                                this.CreateConnector(ma, mventry, rule);
                                // Don't need to process any more rules
                                // as we have just newly provisioned
                                executedMAs.Add(ma.Name);
                                continue;
                            }
                        }
                        else
                        {
                            // The rule isnt a provisioning rule, and there are no connectors
                            // so there is nothing to do - skip to the next rule
                            continue;
                        }
                    }
                    else
                    {
                        // There is at least one connector, so lets see if we should
                        // re-provision, deprovision, rename, or deprovision all

                        if (rule.Action == RuleAction.Rename)
                        {
                            bool hasRenamed = false;

                            foreach (CSEntry renameCandidate in ma.Connectors)
                            {
                                if (this.ConditionsApply(renameCandidate, mventry, rule.ConditionalRename.Conditions))
                                {
                                    this.ConditionalRenameConnector(ma, renameCandidate, mventry, rule);
                                    hasRenamed = true;
                                }
                            }

                            if (hasRenamed)
                            {
                                executedMAs.Add(ma.Name);
                                continue;
                            }
                        }
                        else if (rule.Action == RuleAction.Provision)
                        {
                            // We already have a connector, so check if we need to re-provision

                            if ((rule.Reprovision != null) && (rule.Reprovision.ReprovisionEnabled))
                            {
                                Tracer.TraceInformation("check-reprovisioning-conditions");
                                if (rule.Reprovision.Conditions == null)
                                {
                                    continue;
                                }

                                bool hasReproved = false;

                                foreach (CSEntry reprovCandidate in ma.Connectors.OfType<CSEntry>().ToList())
                                {
                                    if (rule.Reprovision.Conditions.Met(mventry, reprovCandidate))
                                    {
                                        Tracer.TraceInformation("Reprovisioning '{0}' using rule id '{1}'",
                                            reprovCandidate.DN, rule);
                                        this.DeprovisionConnector(ma, reprovCandidate, mventry, rule);
                                        this.CreateConnector(ma, mventry, rule);
                                        hasReproved = true;
                                    }
                                }

                                if (hasReproved)
                                {
                                    executedMAs.Add(ma.Name);
                                    continue;
                                }
                            }
                        }
                        else if (rule.Action == RuleAction.Deprovision)
                        {
                            bool hasDeleted = false;
                            foreach (CSEntry deprovCandidate in ma.Connectors.OfType<CSEntry>().ToList())
                            {
                                if (!this.ConditionsApply(deprovCandidate, mventry, rule.Conditions))
                                {
                                    continue;
                                }

                                this.DeprovisionConnector(ma, deprovCandidate, mventry, rule);
                                hasDeleted = true;
                            }

                            if (hasDeleted)
                            {
                                executedMAs.Add(ma.Name);
                                continue;
                            }
                        }
                        else if (rule.Action == RuleAction.DeprovisionAll)
                        {
                            foreach (CSEntry deprovCandidate in ma.Connectors.OfType<CSEntry>().ToList())
                            {
                                if (!this.ConditionsApply(deprovCandidate, mventry, rule.Conditions))
                                {
                                    continue;
                                }

                                mventry.ConnectedMAs.DeprovisionAll();
                                break;
                            }
                        }
                        else
                        {
                            Tracer.TraceError("invalid-action-specified {0}", rule.Action);
                        }
                    }

                    Tracer.TraceInformation("end-rule {0}", rule.Name);
                }
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-provision");
            }
        }
        public bool ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {
            throw new EntryPointNotImplementedException();
        }
        public void Terminate()
        {
            Tracer.TraceInformation("enter-terminate");
            Tracer.Indent();
            this.EngineRules = null;
            Tracer.TraceInformation("pre-gc-allocated-memory '{0:n}'", GC.GetTotalMemory(true) / 1024M);
            GC.Collect();
            Tracer.TraceInformation("post-gc-allocated-memory '{0:n}'", GC.GetTotalMemory(true) / 1024M);
            Tracer.Unindent();
            Tracer.TraceInformation("exit-terminate");
        }

        #endregion

        private void CreateConnector(ConnectedMA ma, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-createconnector");
            Tracer.Indent();
            try
            {
                Tracer.TraceInformation("create-connector: MV: '{0}', MA: '{1}'", mventry.ObjectID, ma.Name);
                IList<string> additionalObjectClasses = this.GetAdditionalObjectClasses(mventry, rule);
                CSEntry csentry;

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
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-createconnector");
            }
        }
        private void DeprovisionConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            Tracer.TraceInformation("enter-deprovisionconnector");
            Tracer.Indent();
            try
            {
                Tracer.TraceInformation("deprovision-connector: DN: '{0}', MA: '{1}'", csentry.DN, csentry.MA.Name);
                csentry.Deprovision();
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-deprovisionconnector");
            }
        }
        private void ConditionalRenameConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            Tracer.TraceInformation("enter-conditionalrenameconnector");
            Tracer.Indent();
            try
            {
                if (connectorRule.ConditionalRename == null)
                    return;

                string escapedCN = null;
                string replacedValue = null;
                if (string.IsNullOrEmpty(connectorRule.ConditionalRename.EscapedCN))
                {
                    Tracer.TraceInformation("no-cn-to-escape");
                    replacedValue = connectorRule.ConditionalRename.NewDNValue.ReplaceWithMVValueOrBlank(mventry);
                }
                else
                {
                    escapedCN = ma.EscapeDNComponent(connectorRule.ConditionalRename.EscapedCN.ReplaceWithMVValueOrBlank(mventry, "")).ToString();
                    Tracer.TraceInformation("escaped-cn {0}", escapedCN);
                    replacedValue = connectorRule.ConditionalRename.NewDNValue.ReplaceWithMVValueOrBlank(mventry, escapedCN);
                }
                ReferenceValue newdn = ma.CreateDN(replacedValue);
                Tracer.TraceInformation("old-dn '{0}'", csentry.DN.ToString());
                Tracer.TraceInformation("new-dn '{0}'", newdn.ToString());

                if (this.AreDNsEqual(csentry.DN, newdn, ma, connectorRule.ConditionalRename.StrictDNCompare))
                {
                    Tracer.TraceInformation("no-renaming-necessary");
                }
                else
                {
                    Tracer.TraceInformation("dn-rename-required");
                    csentry.DN = newdn;
                }
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-conditionalrenameconnector");
            }
        }
        private bool ConditionsApply(CSEntry csentry, MVEntry mventry, Conditions conditions)
        {
            Tracer.TraceInformation("enter-conditionsapply");
            Tracer.Indent();
            try
            {
                if (conditions == null || conditions.ConditionBase.Count == 0)
                {
                    Tracer.TraceInformation("no-conditions-specified-returning-true");
                    return true;
                }
                return conditions.Met(mventry, csentry);
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-conditionsapply");
            }
        }
        private IList<string> GetAdditionalObjectClasses(MVEntry mventry, Rule connectorRule)
        {
            Tracer.TraceInformation("enter-getadditionalobjectclasses");
            Tracer.Indent();
            List<string> valuesToAdd = new List<string>();
            try
            {
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
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-getadditionalobjectclasses");
            }
            return valuesToAdd;
        }
        private void SetupInitialValues(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
        {
            Tracer.TraceInformation("enter-setupinitialvalues");
            Tracer.Indent();
            try
            {
                if (connectorRule.Helpers != null)
                {
                    Tracer.TraceInformation("generating-helper-values");
                    foreach (HelperValue helper in connectorRule.Helpers)
                    {
                        helper.Generate();
                    }
                }
                foreach (AttributeFlowBase attributeBase in connectorRule.InitialFlows)
                {
                    attributeBase.Generate(ma, csentry, mventry, connectorRule);
                }
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-setupinitialvalues");
            }
        }
        private bool AreDNsEqual(ReferenceValue dn1, ReferenceValue dn2, ManagementAgent ma, bool strictCompare)
        {
            if (strictCompare)
            {
                Tracer.TraceInformation("performing-strict-DN-comparison");
                return dn1.ToString() == dn2.ToString();
            }
            else
            {
                Tracer.TraceInformation("performing-RFC-compliant-DN-comparison");
                return dn1.Equals(dn2);
            }
        }
    }
}
