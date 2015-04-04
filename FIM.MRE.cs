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
// march 11, 2015 | Ryan Newington
//  - updated registry key logic
// april 4, 2015 | soren granfeldt
//	-added Trace logging to all functions
//	-added logging to eventlog for errors and warning

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
		const string EventLogSource = "FIM Metaverse Rules Extension";
		const string EventLogName = "Application";
		const string TraceSourceName = "FIM.MRE";

		static string DebugLogFilename = null;
		private RulesFile EngineRules = new RulesFile();

		#region IMVSynchronization Methods

		public void Initialize()
		{
			try
			{
				Trace.TraceInformation("enter-initialize");

				RegistryKey machineRegistry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
				RegistryKey mreRootKey = machineRegistry.OpenSubKey(@"SOFTWARE\Granfeldt\FIM\MRE", false);

				if (mreRootKey != null)
				{
					string logFileValue = mreRootKey.GetValue("DebugLogFileName", null) as string;
					if (logFileValue != null)
					{
						DebugLogFilename = string.Format(logFileValue, DateTime.Now);

						TextWriterTraceListener i = new TextWriterTraceListener(Path.ChangeExtension(DebugLogFilename, ".log"), TraceSourceName);
						i.TraceOutputOptions = TraceOptions.DateTime;
						i.Filter = new EventTypeFilter(SourceLevels.All);
						Trace.Listeners.Add(i);

						TextWriterTraceListener el = new TextWriterTraceListener(Path.ChangeExtension(DebugLogFilename, ".errors.log"), TraceSourceName);
						el.TraceOutputOptions = TraceOptions.DateTime | TraceOptions.Callstack;
						el.Filter = new EventTypeFilter(SourceLevels.Critical | SourceLevels.Error | SourceLevels.Warning);
						Trace.Listeners.Add(el);
					}
				}

				// https://social.msdn.microsoft.com/Forums/windowsdesktop/en-US/00a043ae-9ea1-4a55-8b7c-d088a4b08f09/how-do-i-create-an-event-log-source-under-vista?forum=windowsgeneraldevelopmentissues
				if (!EventLog.SourceExists(EventLogSource))
				{
					Trace.TraceInformation("creating-eventlog-source '{0}'", EventLogSource);
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
				Trace.Listeners.Add(eventLog);

				string extensionsDirectory = Utils.ExtensionsDirectory;
#if DEBUG
                extensionsDirectory = Directory.GetCurrentDirectory();
#endif
				Trace.TraceInformation("loading-default-rules-file");
				new MVRules().LoadSettingsFromFile(Path.Combine(extensionsDirectory, "fim.mre.xml"), ref this.EngineRules);
				Trace.TraceInformation("loading-additional-rules-files");
				string[] ruleFiles = Directory.GetFiles(extensionsDirectory, "*.fim.mre.xml", SearchOption.TopDirectoryOnly);
				foreach (string ruleFile in ruleFiles)
				{
					RulesFile rules = new RulesFile();
					Trace.TraceInformation("loading-rule-file {0}", ruleFile);
					new MVRules().LoadSettingsFromFile(ruleFile, ref rules);
					foreach (Rule rule in rules.Rules)
					{
						EngineRules.Rules.Add(rule);
					}
				}
				Trace.TraceInformation("evaluating-{0}-rules", EngineRules.Rules.Count);
				foreach (Rule rule in EngineRules.Rules)
				{
					if (rule.Enabled && rule.RenameDnFlow != null)
					{
						Trace.TraceWarning("RenameConnector XML is obsolete and will be removed from coming version.");
					}

					if (rule.Enabled)
					{
						Trace.TraceInformation("found-active-rule {0}", rule.Name);
					}
					else
					{
						Trace.TraceInformation("found-inactive-rule {0}", rule.Name);
					}
				}
				Trace.TraceInformation("removing-inactive-{0}-rules", EngineRules.Rules.Count(ru => !ru.Enabled));
				EngineRules.Rules.RemoveAll(rule => !rule.Enabled);
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex);
				throw;
			}
			finally
			{
				Trace.TraceInformation("exit-initialize");
			}
		}
		public void Provision(MVEntry mventry)
		{
			Trace.TraceInformation("enter-provision");
			if (EngineRules.DisableAllRules)
			{
				Trace.TraceInformation("provisioning-is-disabled");
				return;
			}
			try
			{
				foreach (Rule rule in EngineRules.Rules.Where(mv => mv.SourceObject.Equals(mventry.ObjectType, StringComparison.OrdinalIgnoreCase)))
				{
					Trace.TraceInformation("start-rule '{0}' (MA {1})", rule.Name, rule.TargetManagementAgentName);
					Trace.TraceInformation("Object ({0}): {1} (GUID {2})", mventry.ObjectType, mventry["displayName"].IsPresent ? mventry["displayName"].Value : "", mventry.ObjectID);
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
									Trace.TraceError("more-than-one-connector-(" + ma.Connectors.Count + ")-exists");
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
							Trace.TraceError("invalid-action-specified");
							break;
					}
					Trace.TraceInformation("end-rule {0}", rule.Name);
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex);
				throw;
			}
			finally
			{
				Trace.TraceInformation("exit-provision");
			}
		}
		public bool ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
		{
			throw new EntryPointNotImplementedException();
		}
		public void Terminate()
		{
			Trace.TraceInformation("terminate");
		}

		#endregion

		public void CreateConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
		{
			Trace.TraceInformation("enter-createconnector");
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
				Trace.TraceError("error {0}", ex);
				throw;
			}
			finally
			{
				Trace.TraceInformation("exit-createconnector");
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
			Trace.TraceInformation("enter-deprovisionconnector");
			try
			{
				csentry.Deprovision();
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex);
				throw;
			}
			finally
			{
				Trace.TraceInformation("exit-deprovisionconnector");
			}
		}
		public void ConditionalRenameConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
		{
			Trace.TraceInformation("enter-conditionalrenameconnector");
			try
			{
				if (connectorRule.ConditionalRename == null)
					return;

				string escapedCN = null;
				string replacedValue = null;
				if (string.IsNullOrEmpty(connectorRule.ConditionalRename.EscapedCN))
				{
					Trace.TraceInformation("no-cn-to-escape");
					replacedValue = ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.NewDNValue, mventry);
				}
				else
				{
					escapedCN = ma.EscapeDNComponent(ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.EscapedCN, mventry, "")).ToString();
					Trace.TraceInformation("escaped-cn {0}", escapedCN);
					replacedValue = ReplaceWithMVValueOrBlank(connectorRule.ConditionalRename.NewDNValue, mventry, escapedCN);
				}
				ReferenceValue newdn = ma.CreateDN(replacedValue);
				Trace.TraceInformation("old-dn {0}", csentry.DN.ToString());
				Trace.TraceInformation("new-dn {0}", newdn.ToString());
				if (csentry.DN.ToString().Equals(newdn.ToString()))
				{
					Trace.TraceInformation("no-renaming-necessary");
				}
				else
				{
					csentry.DN = ma.CreateDN(replacedValue);
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex);
				throw;
			}
			finally
			{
				Trace.TraceInformation("exit-conditionalrenameconnector");
			}
		}

		[Obsolete("Use ConditionalRename instead")]
		public void RenameConnector(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
		{
			if (connectorRule.RenameDnFlow == null)
				return;

			Trace.TraceInformation("enter-renameconnector");
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
								Trace.TraceInformation("Reprovisioning {0}", csentry.DN.ToString());
								Trace.TraceInformation("Source(mv): {0}", mventry[connectorRule.RenameDnFlow.Source].Value);
								Trace.TraceInformation("Target(cs): {0}", mventry[connectorRule.RenameDnFlow.Target].Value);
								DeprovisionConnector(ma, csentry, mventry, connectorRule);
								CreateConnector(ma, csentry, mventry, connectorRule);
							}
							else
							{
								Trace.TraceInformation("No difference in source and target; no reprovisioning will be done {0}", csentry.DN.ToString());
							}
						}
						else
						{
							if (connectorRule.RenameDnFlow.Target.ToUpper() == "[DN]")
							{
								Trace.TraceInformation("Current CS:DN is '{0}'", csentry.DN.ToString());
								Trace.TraceInformation("Renaming to MV::{0}, value: '{1}'", connectorRule.RenameDnFlow.Source, mventry[connectorRule.RenameDnFlow.Source].Value);
								if (mventry[connectorRule.RenameDnFlow.Source].IsPresent)
								{
									csentry.DN = ma.CreateDN(mventry[connectorRule.RenameDnFlow.Source].Value);
								}
								else
								{
									Trace.TraceInformation("{0} is not present in metaverse", connectorRule.RenameDnFlow.Source);
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
						Trace.TraceInformation("{0} is null, empty or not present in metaverse", connectorRule.RenameDnFlow.Source);
					}
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex);
				throw;
			}
			finally
			{
				Trace.TraceInformation("exit-renameconnector");
			}
		}

		public bool ConditionsApply(CSEntry csentry, MVEntry mventry, Conditions conditions)
		{
			Trace.TraceInformation("enter-conditionsapply");
			try
			{
				if (conditions == null || conditions.ConditionBase.Count == 0)
				{
					Trace.TraceInformation("No conditions specified (returning true)");
					return true;
				}
				return conditions.Met(mventry, csentry);
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex);
				throw;
			}
			finally
			{
				Trace.TraceInformation("exit-conditionsapply");
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
				Trace.TraceInformation("replaced '{0}' with '{1}'", matchValue, newValue);
				source = Regex.Replace(source, string.Format(@"#mv\:{0}", match.Value), mventry[matchValue].Value);
			}
			return source;
		}

		public void SetupInitialValues(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule connectorRule)
		{
			Trace.TraceInformation("enter-setupinitialvalues");
			try
			{
				foreach (AttributeFlowBase attributeBase in connectorRule.InitialFlows)
				{
					if (attributeBase.GetType() == typeof(AttributeFlowGuid))
					{
						AttributeFlowGuid attribute = (AttributeFlowGuid)attributeBase;
						Guid newGuid = Guid.NewGuid();
						Trace.TraceInformation("New GUID {0} to {1}", newGuid.ToString(), attribute.Target);

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

						Trace.TraceInformation("Flow source value: '" + mventry[attrFlow.Source].Value + "'");
						Trace.TraceInformation("Target value: '" + TargetValue + "'");
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
							Trace.TraceInformation("No CN to escape");
							replacedValue = ReplaceWithMVValueOrBlank(attrFlow.Constant, mventry);
						}
						else
						{
							escapedCN = ma.EscapeDNComponent(ReplaceWithMVValueOrBlank(attrFlow.EscapedCN, mventry, "")).ToString();
							Trace.TraceInformation("EscapedCN", escapedCN);
							replacedValue = ReplaceWithMVValueOrBlank(attrFlow.Constant, mventry, escapedCN);
						}
						Trace.TraceInformation("Flow constant '{0}' to {1}", replacedValue, attrFlow.Target);
						if (attrFlow.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
							csentry.DN = csentry.MA.CreateDN(replacedValue);
						else
							csentry[(attrFlow.Target)].Value = replacedValue;
						continue;
					}

					if (attributeBase.GetType() == typeof(AttributeFlowConcatenate))
					{
						Trace.TraceInformation("Building concatenated value");
						AttributeFlowConcatenate attrFlow = (AttributeFlowConcatenate)attributeBase;
						string concatValue = null;
						foreach (SourceExpressionBase sourceExpression in attrFlow.SourceExpressions)
						{
							if (sourceExpression.GetType() == typeof(SourceExpressionConstant))
							{
								SourceExpressionConstant sourceExpr = (SourceExpressionConstant)sourceExpression;
								string replacedValue = ReplaceWithMVValueOrBlank(sourceExpr.Source, mventry);
								Trace.TraceInformation("Adding constant '{0}'", replacedValue);
								concatValue = concatValue + replacedValue;
								continue;
							}
							if (sourceExpression.GetType() == typeof(SourceExpressionRegexReplace))
							{
								SourceExpressionRegexReplace sourceExpr = (SourceExpressionRegexReplace)sourceExpression;
								Trace.TraceInformation("Adding RegEx replacement '{0}'", sourceExpr.Source);
								if (mventry[sourceExpr.Source].IsPresent)
								{
									concatValue = concatValue + Regex.Replace(mventry[sourceExpr.Source].Value, sourceExpr.Pattern, sourceExpr.Replacement);
								}
								else
								{
									Trace.TraceError("attribute-'" + sourceExpr.Source + "'-is-not-present-in-metaverse");
								}
								continue;
							}
							if (sourceExpression.GetType() == typeof(SourceExpressionAttribute))
							{
								SourceExpressionAttribute attr = (SourceExpressionAttribute)sourceExpression;
								if (mventry[attr.Source].IsPresent)
								{
									Trace.TraceInformation("Adding value of MV::{0} '{1}'", attr.Source, mventry[attr.Source].Value);
									concatValue = concatValue + mventry[attr.Source].Value.ToString();
								}
								else
								{
									Trace.TraceError("attribute-'" + attr.Source + "'-is-not-present-in-metaverse");
								}
								continue;
							}
						}
						Trace.TraceInformation("Flow concatenated attribute value '{0}' to {1}", concatValue, attrFlow.Target);
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
				Trace.TraceError("error {0}", ex);
				throw;
			}
			finally
			{
				Trace.TraceInformation("exit-setupinitialvalues");
			}
		}
	}
}
