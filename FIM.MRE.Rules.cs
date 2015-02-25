using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    public class RulesFile
    {
        public bool DisableAllRules = false;
        public List<Rule> Rules;
        public RulesFile()
        {
            this.Rules = new List<Rule>();
        }
    }

    public class Rule
    {
        public string Action;
        public Conditions Conditions;
        public string Description = "";
        public bool Enabled = false;
        public List<AttributeFlowBase> InitialFlows;
        public string Name = "";
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
        public ConditionOperator Operator { get; set; }
        [XmlElement]
        public List<ConditionBase> ConditionBase { get; set; }
        public Conditions()
        {
            this.ConditionBase = new List<ConditionBase>();
        }
    }

    [XmlInclude(typeof(ConditionAttributeIsPresent)), XmlInclude(typeof(ConditionMatch)), XmlInclude(typeof(ConditionNotMatch)), XmlInclude(typeof(ConditionAttributeIsNotPresent)), XmlInclude(typeof(ConditionConnectedTo)), XmlInclude(typeof(ConditionNotConnectedTo)), XmlIncludeAttribute(typeof(ConditionIsPresent)), XmlIncludeAttribute(typeof(ConditionIsNotPresent))]
    public class ConditionBase
    {
        public string Description = "";
        public Conditions Conditions { get; set; }
        public ConditionBase()
        {
            this.Conditions = new Conditions();
        }
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
