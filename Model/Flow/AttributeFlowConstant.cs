namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Text.RegularExpressions;
    public class AttributeFlowConstant : AttributeFlowBase
    {
        public string EscapedCN;
        public string Constant;

        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowconstant");
            base.Generate(ma, csentry, mventry, rule);
            try
            {
                string escapedCN = null;
                string replacedValue = null;
                replacedValue = this.Constant.ReplaceWithHelperValuesOrBlank(rule.Helpers);

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
                Tracer.TraceInformation("flow-constant-'{0}'-to-'{1}'", replacedValue, this.Target);
                if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                    csentry.DN = csentry.MA.CreateDN(replacedValue);
                else
                    csentry[(this.Target)].Value = replacedValue;
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.TraceInformation("exit-attributeflowconstant");
            }
        }
    }
}
