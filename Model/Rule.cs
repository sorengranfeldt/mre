// april 26, 2015 | soren granfeldt
//	-fixed loading of subconditions
//	-added backwards compatibility fixing to rule class
//	-added new Flow class that later will replace AttributeFlowBase
//		-will support Transforms in later versions

namespace Granfeldt
{
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Xml.Serialization;

	public enum RuleAction
	{
		[XmlEnum("Provision")]
		Provision,
		[XmlEnum("Deprovision")]
		Deprovision,
		[XmlEnum("DeprovisionAll")]
		DeprovisionAll,
		[XmlEnum("Rename")]
		Rename,
		[XmlEnum("provision")]
		provision,
		[XmlEnum("deprovision")]
		deprovision,
		[XmlEnum("deprovisionall")]
		deprovisionall,
		[XmlEnum("rename")]
		rename
	}

	public class Rule
	{
		[XmlElement]
		public RuleAction Action { get; set; }
		public bool Enabled = false;

		public string RuleId;
		public string Name;
		public string Description;

		public Conditions Conditions;

		[XmlArrayItem("Flow", typeof(Flow))]
		[XmlArrayItem("AttributeFlowBase", typeof(AttributeFlowBase))]
		public List<AttributeFlowBase> InitialFlows;

#pragma warning disable 0612, 0618
		public RenameDnFlow RenameDnFlow;
		public RenameAction ConditionalRename;
		public Reprovision Reprovision;

		[XmlArrayItem("Constant", Type = typeof(HelperValueConstant))]
		[XmlArrayItem("ScopedGuid", Type = typeof(HelperValueScopedGuid))]
		public List<HelperValue> Helpers;

		public string SourceObject;
		public string TargetManagementAgentName;
		public string TargetObject;
		public string TargetObjectAdditionalClasses;

		public void EnsureBackwardsCompatibility()
		{
			if (this.RenameDnFlow != null)
			{
				Trace.TraceWarning("'RenameDnFlow' is no longer supported. Use 'ConditionalRename' instead' or remove 'RenameDnFlow' element from rule definition (see documentation).");
			}
			switch (this.Action)
			{
				case RuleAction.provision:
					this.Action = RuleAction.Provision;
					Trace.TraceWarning("Lowercase 'provision' action keyword is deprecated. Use 'Provision' instead' (see documentation).");
					break;
				case RuleAction.deprovision:
					this.Action = RuleAction.Deprovision;
					Trace.TraceWarning("Lowercase 'deprovision' action keyword is deprecated. Use 'Deprovision' instead' (see documentation).");
					break;
				case RuleAction.rename:
					this.Action = RuleAction.Rename;
					Trace.TraceWarning("Lowercase 'rename' action keyword is deprecated. Use 'Rename' instead' (see documentation).");
					break;
				case RuleAction.deprovisionall:
					this.Action = RuleAction.DeprovisionAll;
					Trace.TraceWarning("Lowercase 'deprovisionall' action keyword is deprecated. Use 'DeprovisionAll' instead' (see documentation).");
					break;
				default:
					break;
			}
		}

		public Rule()
		{
			this.Conditions = new Conditions();
			this.InitialFlows = new List<AttributeFlowBase>();
			this.Helpers = new List<HelperValue>();
		}
	}
}
