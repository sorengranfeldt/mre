// february 26, 2015 | soren granfeldt
//  - marked ConditionAttributeIsPresent and ConditionAttributeNotIsPresent as obsolete
// march 7, 2015 | soren granfeldt
//  -added options for future use of operators and/or on conditions
// april 4, 2015 | soren granfeldt
//	-added Trace logging to all functions

using Microsoft.MetadirectoryServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace Granfeldt
{
    #region Rules

    public class MVRules
    {
        #region Methods

        public void LoadSettingsFromFile(string Filename, ref RulesFile Rules)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(RulesFile));
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
            catch (Exception ex)
            {
                Exception exception = ex;
                throw;
            }
        }

        #endregion
    }

    public class RulesFile
    {
        public bool DisableAllRules = false;
        public List<Rule> Rules;
        public RulesFile()
        {
            this.Rules = new List<Rule>();
        }
    }

    public enum RuleAction
    {
        [XmlEnum("Provision")]
        Provision,
        [XmlEnum("Deprovision")]
        Deprovision,
        [XmlEnum("DeprovisionAll")]
        DeprovisionAll,
        [XmlEnum("Rename")]
        Rename
    }

    public class Rule
    {
        [XmlElement]
        public RuleAction Action { get; set; }
        public bool Enabled = false;

        public string Name = "";
        public string Description = "";

        public Conditions Conditions;
        public List<AttributeFlowBase> InitialFlows;

        public RenameDnFlow RenameDnFlow = null;
        public RenameAction ConditionalRename = null;

        public string SourceObject;
        public string TargetManagementAgentName = "";
        public string TargetObject;
        public string TargetObjectAdditionalClasses = null;

        public Rule()
        {
            this.Conditions = new Conditions();
            this.InitialFlows = new List<AttributeFlowBase>();
        }
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
        public Conditions Conditions;
        public RenameAction()
        {
            this.Conditions = new Conditions();
        }
    }

    [Obsolete("Use RenameAction instead")]
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

    #region Conditions

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
            if (Operator.Equals(ConditionOperator.And))
            {
                bool met = true;
                foreach (ConditionBase condition in ConditionBase)
                {
                    met = condition.Met(mventry, csentry);
                    Trace.TraceInformation("'And' condition '{0}' returned: {1}", condition.GetType(), met);
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
					Trace.TraceInformation("'Or' condition '{0}' returned: {1}", condition.GetType(), met);
                    if (met == true) break;
                }
				Trace.TraceInformation("One or more 'Or' conditions {0} met", met ? "were" : "were not");
                return met;
            }
        }
    }

    [XmlInclude(typeof(ConditionAttributeIsPresent)), XmlInclude(typeof(ConditionMatch)), XmlInclude(typeof(ConditionNotMatch)), XmlInclude(typeof(ConditionAttributeIsNotPresent)), XmlInclude(typeof(ConditionConnectedTo)), XmlInclude(typeof(ConditionNotConnectedTo)), XmlIncludeAttribute(typeof(ConditionIsPresent)), XmlIncludeAttribute(typeof(ConditionIsNotPresent)), XmlInclude(typeof(SubCondition))]
    public class ConditionBase
    {
        public string Description = "";

        public virtual bool Met(MVEntry mventry, CSEntry csentry)
        {
            return true;
        }
    }

    public class ConditionIsPresent : ConditionBase
    {
        public string MVAttribute = "";

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
        public string MVAttribute = "";
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
        public string MVAttribute = "";
        public string Pattern = "";

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
        public string MVAttribute = "";
        public string Pattern = "";

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

    public class ConditionConnectedTo : ConditionBase
    {
        public string ManagementAgentName = "";

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
        public string ManagementAgentName = "";

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

        [XmlElement]
        public List<ConditionBase> ConditionBase { get; set; }

        public SubCondition()
        {
            this.ConditionBase = new List<ConditionBase>();
        }

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (Operator.Equals(ConditionOperator.And))
            {
                bool met = true;
                foreach (ConditionBase condition in ConditionBase)
                {
                    met = condition.Met(mventry, csentry);
					Trace.TraceInformation("'And' condition '{0}' returned: {1}", condition.GetType(), met);
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
					Trace.TraceInformation("'Or' condition '{0}' returned: {1}", condition.GetType(), met);
                    if (met == true) break;
                }
				Trace.TraceInformation("One or more 'Or' conditions {0} met", met ? "were" : "were not");
                return met;
            }
        }
    }

    [Obsolete]
    public class ConditionAttributeIsPresent : ConditionBase
    {
        public string MVAttribute = "";

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
        public string MVAttribute = "";

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
