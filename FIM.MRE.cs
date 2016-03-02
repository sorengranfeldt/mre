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

		private RulesFile EngineRules = new RulesFile();

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
				this.EngineRules = new RulesFile();
				string[] ruleFiles = Directory.GetFiles(ruleFilesPath, "*fim.mre.xml", SearchOption.AllDirectories);
				foreach (string ruleFile in ruleFiles)
				{
					RulesFile rules = new RulesFile();
					Tracer.TraceInformation("loading-rule-file '{0}'", ruleFile);
					new MVRules().LoadSettingsFromFile(ruleFile, ref rules);
					foreach (Rule rule in rules.Rules)
					{
						EngineRules.Rules.Add(rule);
					}
				}
				Tracer.TraceInformation("evaluating-{0}-rule(s)", EngineRules.Rules.Count);
				foreach (Rule rule in EngineRules.Rules)
				{
					rule.EnsureBackwardsCompatibility();

					if (rule.Enabled && rule.RenameDnFlow != null)
					{
						Tracer.TraceWarning("RenameDnFlow XML element is obsolete and will not be supported in coming versions. Please see documentation for more information.");
					}
					if (rule.Enabled)
					{
						Tracer.TraceInformation("found-active-rule {0}", rule.Name);
					}
					else
					{
						Tracer.TraceInformation("found-inactive-rule {0}", rule.Name);
					}
				}
				Tracer.TraceInformation("removing-inactive-{0}-rules", EngineRules.Rules.Count(ru => !ru.Enabled));
				EngineRules.Rules.RemoveAll(rule => !rule.Enabled);
				if (EngineRules.Rules.Count.Equals(0))
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
			try
			{
				foreach (Rule rule in EngineRules.Rules.Where(mv => mv.SourceObject.Equals(mventry.ObjectType, StringComparison.OrdinalIgnoreCase)))
				{
					Tracer.TraceInformation("start-rule '{0}' (MA {1})", rule.Name, rule.TargetManagementAgentName);
					Tracer.TraceInformation("{0}, displayname: {1}, mvguid: {2}", mventry.ObjectType, mventry["displayName"].IsPresent ? mventry["displayName"].Value : "n/a", mventry.ObjectID);
					CSEntry csentry = null;
					ConnectedMA ma = mventry.ConnectedMAs[rule.TargetManagementAgentName];
					switch (rule.Action)
					{
						case RuleAction.Rename:
							if (ma.Connectors.Count == 1 && ConditionsApply(csentry, mventry, rule.ConditionalRename.Conditions))
							{
								csentry = ma.Connectors.ByIndex[0];
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
									Tracer.TraceInformation("already-connected {0}", csentry.DN);
									if (rule.Reprovision != null && rule.Reprovision.ProvisionRuleId != null)
									{
										Rule reprovRule = EngineRules.Rules.Where(r => r.RuleId.Equals(rule.Reprovision.ProvisionRuleId)).FirstOrDefault();
										if (reprovRule != null)
										{
											Tracer.TraceInformation("check-reprovisioning-conditions");
											if (rule.Reprovision.Conditions != null && rule.Reprovision.Conditions.Met(mventry, csentry))
											{
												Tracer.TraceInformation("reprovisioning '{0}' using rule id '{1}'", csentry.DN, rule.Reprovision.ProvisionRuleId);
												DeprovisionConnector(ma, csentry, mventry, rule);
												CreateConnector(ma, csentry, mventry, reprovRule);
											}
											else
											{
												Tracer.TraceInformation("reprovisioning-conditions-not-met");
											}
										}
										else
										{
											Tracer.TraceError("reprovisioning-rule-not-found: id '{0}'", rule.Reprovision.ProvisionRuleId);
										}
									}
									this.ConditionalRenameConnector(ma, csentry, mventry, rule);
								}
								else
								{
									Tracer.TraceError("more-than-one-connector-(" + ma.Connectors.Count + ")-exists");
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
							Tracer.TraceError("invalid-action-specified {0}", rule.Action);
							break;
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
			EngineRules = null;
			Tracer.TraceInformation("pre-gc-allocated-memory '{0:n}'", GC.GetTotalMemory(true) / 1024M);
			GC.Collect();
			Tracer.TraceInformation("post-gc-allocated-memory '{0:n}'", GC.GetTotalMemory(true) / 1024M);
			Tracer.Unindent();
			Tracer.TraceInformation("exit-terminate");
		}

		#endregion

		private void CreateConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
		{
			Tracer.TraceInformation("enter-createconnector");
			Tracer.Indent();
			try
			{
				Tracer.TraceInformation("create-connector: MV: '{0}', MA: '{1}'", mventry.ObjectID, ma.Name);
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
					csentry.DN = ma.CreateDN(replacedValue);
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
