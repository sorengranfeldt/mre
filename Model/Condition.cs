
namespace Granfeldt
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Text.RegularExpressions;
	using System.Xml.Serialization;
	using Microsoft.MetadirectoryServices;

	public enum ConditionOperator
	{
		[XmlEnum("And")]
		And,
		[XmlEnum("Or")]
		Or
	}

	public class Conditions
	{
		[XmlAttribute]
		[XmlTextAttribute()]
		public ConditionOperator Operator { get; set; }
		[XmlElement]
		public List<ConditionBase> ConditionBase { get; set; }

		public Conditions()
		{
			this.ConditionBase = new List<ConditionBase>();
		}
		public virtual bool Met(MVEntry mventry, CSEntry csentry)
		{
			Trace.TraceInformation("enter-conditionsmet");
			Trace.Indent();
			try
			{
				if (Operator.Equals(ConditionOperator.And))
				{
					bool met = true;
					foreach (ConditionBase condition in ConditionBase)
					{
						met = condition.Met(mventry, csentry);
						Trace.TraceInformation("'And' condition '{0}'/{1} returned: {2}", condition.GetType().Name, condition.Description, met);
						if (met == false) break;
					}
					Trace.TraceInformation("All 'And' conditions {0} met", met ? "were" : "were not");
					return met;
				}
				else
				{
					bool met = false;
					foreach (ConditionBase condition in ConditionBase)
					{
						met = condition.Met(mventry, csentry);
						Trace.TraceInformation("'Or' condition '{0}'/{1} returned: {2}", condition.GetType().Name, condition.Description, met);
						if (met == true) break;
					}
					Trace.TraceInformation("One or more 'Or' conditions {0} met", met ? "were" : "were not");
					return met;
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex.GetBaseException());
				throw;
			}
			finally
			{
				Trace.Unindent();
				Trace.TraceInformation("exit-conditionsmet");
			}
		}
	}

#pragma warning disable 0612, 0618
	[
	XmlInclude(typeof(ConditionAttributeIsNotPresent)), XmlInclude(typeof(ConditionAttributeIsPresent)),
	XmlInclude(typeof(ConditionMatch)), XmlInclude(typeof(ConditionNotMatch)),
	XmlInclude(typeof(ConditionConnectedTo)), XmlInclude(typeof(ConditionNotConnectedTo)),
	XmlInclude(typeof(ConditionIsPresent)), XmlInclude(typeof(ConditionIsNotPresent)),
	XmlInclude(typeof(ConditionAreEqual)), XmlInclude(typeof(ConditionAreNotEqual)),
	XmlInclude(typeof(ConditionIsTrue)), XmlInclude(typeof(ConditionIsFalse)),
	XmlInclude(typeof(SubCondition))
	]
	public class ConditionBase
	{
		public string Description;

		public virtual bool Met(MVEntry mventry, CSEntry csentry)
		{
			return true;
		}
	}

	public class ConditionIsPresent : ConditionBase
	{
		public string MVAttribute;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			if (mventry[this.MVAttribute].IsPresent)
			{
				return true;
			}
			else
			{
				Trace.TraceInformation("Condition failed (Reason: Metaverse attribute value is present) {0}", this.Description);
				return false;
			}
		}
	}
	public class ConditionIsNotPresent : ConditionBase
	{
		public string MVAttribute;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			if (!mventry[this.MVAttribute].IsPresent)
			{
				return true;
			}
			else
			{
				Trace.TraceInformation("Condition failed (Reason: Metaverse attribute value is present) {0}", this.Description);
				return false;
			}
		}
	}

	public class ConditionMatch : ConditionBase
	{
		public string MVAttribute;
		public string Pattern;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			if (!mventry[this.MVAttribute].IsPresent)
			{
				Trace.TraceInformation("Condition failed (Reason: No metaverse value is present) {0}", this.Description);
				return false;
			}
			else
			{
				if (!Regex.IsMatch(mventry[this.MVAttribute].Value, this.Pattern, RegexOptions.IgnoreCase))
				{
					Trace.TraceInformation("Condition failed (Reason: RegEx doesnt match) {0}", this.Description);
					return false;
				}
				return true;
			}
		}
	}
	public class ConditionNotMatch : ConditionBase
	{
		public string MVAttribute;
		public string Pattern;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			if (mventry[this.MVAttribute].IsPresent)
			{
				if (Regex.IsMatch(mventry[this.MVAttribute].Value, this.Pattern, RegexOptions.IgnoreCase))
				{
					Trace.TraceInformation("Condition failed (Reason: RegEx match) {0}", this.Description);
					return false;
				}
			}
			return true; // value not present effectively means not-match
		}
	}

	public class ConditionIsTrue : ConditionBase
	{
		public string MVAttribute;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			if (!mventry[this.MVAttribute].IsPresent)
			{
				Trace.TraceInformation("Condition failed (Reason: No metaverse value is present) {0}", this.Description);
				return false;
			}

			if (!mventry[this.MVAttribute].BooleanValue)
			{
				Trace.TraceInformation("Condition failed (Reason: Boolean value is false) {0}", this.Description);
				return false;
			}
			return true;
		}
	}
	public class ConditionIsFalse : ConditionBase
	{
		public string MVAttribute;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			if (!mventry[this.MVAttribute].IsPresent)
			{
				Trace.TraceInformation("Condition failed (Reason: No metaverse value is present) {0}", this.Description);
				return false;
			}

			if (mventry[this.MVAttribute].BooleanValue)
			{
				Trace.TraceInformation("Condition failed (Reason: Boolean value is true) {0}", this.Description);
				return false;
			}
			return true;
		}
	}

	public class ConditionAreEqual : ConditionBase
	{
		public string MVAttribute;
		public string CSAttribute;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			string csValue = csentry[CSAttribute].IsPresent ? csentry[CSAttribute].Value : null;
			string mvValue = mventry[MVAttribute].IsPresent ? mventry[MVAttribute].Value : null;

			if (csValue.Equals(MVAttribute))
			{
				Trace.TraceInformation("Condition failed (Reason: Values are not equal) {0}", this.Description);
				return false;
			}
			return true;
		}
	}
	public class ConditionAreNotEqual : ConditionBase
	{
		public string MVAttribute;
		public string CSAttribute;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			string csValue = csentry[CSAttribute].IsPresent ? csentry[CSAttribute].Value : null;
			string mvValue = mventry[MVAttribute].IsPresent ? mventry[MVAttribute].Value : null;

			if (csValue != MVAttribute)
			{
				Trace.TraceInformation("Condition failed (Reason: Values are equal) {0}", this.Description);
				return false;
			}
			return true;
		}
	}

	public class ConditionConnectedTo : ConditionBase
	{
		public string ManagementAgentName;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			ConnectedMA MA = mventry.ConnectedMAs[this.ManagementAgentName];
			if (MA.Connectors.Count.Equals(0))
			{
				Trace.TraceInformation("Condition failed (Reason: Not connected to {0}) {1}", this.ManagementAgentName, this.Description);
				return false;
			}
			return true;
		}
	}
	public class ConditionNotConnectedTo : ConditionBase
	{
		public string ManagementAgentName;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			ConnectedMA MA = mventry.ConnectedMAs[this.ManagementAgentName];
			if (MA.Connectors.Count > 0)
			{
				Trace.TraceInformation("Condition failed (Reason: Still connected to {0}) {1}", this.ManagementAgentName, this.Description);
				return false;
			}
			return true;
		}
	}

	public class SubCondition : ConditionBase
	{
		[XmlAttribute]
		[XmlTextAttribute()]
		public ConditionOperator Operator { get; set; }

		[XmlElement(ElementName = "ConditionBase")]
		public List<ConditionBase> ConditionBase { get; set; }

		public SubCondition()
		{
			this.ConditionBase = new List<ConditionBase>();
		}

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			Trace.TraceInformation("enter-subconditionsmet");
			Trace.Indent();
			try
			{
				if (Operator.Equals(ConditionOperator.And))
				{
					bool met = true;
					foreach (ConditionBase condition in ConditionBase)
					{
						met = condition.Met(mventry, csentry);
						Trace.TraceInformation("'And' condition '{0}'/{1} returned: {2}", condition.GetType().Name, condition.Description, met);
						if (met == false) break;
					}
					Trace.TraceInformation("All 'And' conditions {0} met", met ? "were" : "were not");
					return met;
				}
				else
				{
					bool met = false;
					foreach (ConditionBase condition in ConditionBase)
					{
						met = condition.Met(mventry, csentry);
						Trace.TraceInformation("'Or' condition '{0}'/{1} returned: {2}", condition.GetType().Name, condition.Description, met);
						if (met == true) break;
					}
					Trace.TraceInformation("One or more 'Or' conditions {0} met", met ? "were" : "were not");
					return met;
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex.GetBaseException());
				throw;
			}
			finally
			{
				Trace.Unindent();
				Trace.TraceInformation("exit-subconditionsmet");
			}
		}
	}

	#region obsolete

	[Obsolete]
	public class ConditionAttributeIsPresent : ConditionBase
	{
		public string MVAttribute;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			Trace.TraceWarning("Condition type 'ConditionAttributeIsPresent' is obsolete. Please see documentation.");
			if (mventry[this.MVAttribute].IsPresent)
			{
				return true;
			}
			else
			{
				Trace.TraceInformation("Condition failed (Reason: Metaverse attribute value is present) {0}", this.Description);
				return false;
			}
		}
	}
	[Obsolete]
	public class ConditionAttributeIsNotPresent : ConditionBase
	{
		public string MVAttribute;

		public override bool Met(MVEntry mventry, CSEntry csentry)
		{
			Trace.TraceWarning("Condition type 'ConditionAttributeIsNotPresent' is obsolete. Please see documentation.");
			if (!mventry[this.MVAttribute].IsPresent)
			{
				return true;
			}
			else
			{
				Trace.TraceInformation("Condition failed (Reason: Metaverse attribute value is present) {0}", this.Description);
				return false;
			}
		}
	}

	#endregion

}
