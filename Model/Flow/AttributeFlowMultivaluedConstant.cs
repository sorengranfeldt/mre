namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Xml.Serialization;

    public class AttributeFlowMultivaluedConstant : AttributeFlowBase
    {
        public string EscapedCN;
        [XmlArray("Constants")]
        [XmlArrayItem("Constant")]
        public List<string> Constants;

        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowmultivaluedconstant");

            if (Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cannot use a multivalued constant flow on the DN of an object");
            }

            if (this.Constants == null)
            {
                throw new ArgumentException("The <Constants> element must be present with one or more values when using a multivalued constant attribute flow rule");
            }

            base.Generate(ma, csentry, mventry, rule);

            try
            {
                foreach (string constant in this.Constants)
                {
                    string escapedCN = null;
                    string replacedValue = null;
                    replacedValue = constant.ReplaceWithHelperValuesOrBlank(rule.Helpers);

                    if (string.IsNullOrEmpty(this.EscapedCN))
                    {
                        Tracer.TraceInformation("no-CN-to-escape");
                        replacedValue = replacedValue.ReplaceWithMVValueOrBlank(mventry);
                    }
                    else
                    {
                        escapedCN = this.EscapedCN.ReplaceWithHelperValuesOrBlank(rule.Helpers);
                        escapedCN = ma.EscapeDNComponent(this.EscapedCN.ReplaceWithMVValueOrBlank(mventry, "")).ToString();
                        Tracer.TraceInformation("escaped-cn '{0}'", escapedCN);
                        replacedValue = replacedValue.ReplaceWithMVValueOrBlank(mventry, escapedCN);
                    }
                    Tracer.TraceInformation("flow-mv-constant-'{0}'-to-'{1}'", replacedValue, this.Target);

                    csentry[(this.Target)].Values.Add(replacedValue);
                }
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.TraceInformation("exit-attributeflowmutlivaluedconstant");
            }
        }
    }
}
