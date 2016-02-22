// october 30, 2015 | soren granfeldt
//	-added possibility to use MV ObjectID in flow attribute

namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using System.Xml.Serialization;
    using System.Collections.Generic;

    [
    XmlInclude(typeof(AttributeFlowAttribute)),
    XmlInclude(typeof(AttributeFlowConcatenate)),
    XmlInclude(typeof(AttributeFlowConstant)),
    XmlInclude(typeof(AttributeFlowMultivaluedConstant)),
    XmlInclude(typeof(AttributeFlowGuid)),
    XmlInclude(typeof(Flow)),
    ]
    public class AttributeFlowBase
    {
        public string Description;
        public string Target;

        public virtual void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            //intentionally left blank
        }
    }

    public class AttributeFlowConcatenate : AttributeFlowBase
    {
        public SourceExpressionBase[] SourceExpressions;

        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowconcatenate");
            Tracer.Indent();
            base.Generate(ma, csentry, mventry, rule);
            try
            {
                string concatValue = null;
                foreach (SourceExpressionBase sourceExpression in this.SourceExpressions)
                {
                    if (sourceExpression.GetType() == typeof(SourceExpressionConstant))
                    {
                        SourceExpressionConstant sourceExpr = (SourceExpressionConstant)sourceExpression;
                        string replacedValue = sourceExpr.Source.ReplaceWithMVValueOrBlank(mventry);
                        Tracer.TraceInformation("adding-constant-'{0}'", replacedValue);
                        concatValue = concatValue + replacedValue;
                        continue;
                    }
                    if (sourceExpression.GetType() == typeof(SourceExpressionRegexReplace))
                    {
                        SourceExpressionRegexReplace sourceExpr = (SourceExpressionRegexReplace)sourceExpression;
                        Tracer.TraceInformation("adding-regex-replacement-'{0}'", sourceExpr.Source);
                        if (mventry[sourceExpr.Source].IsPresent)
                        {
                            concatValue = concatValue + Regex.Replace(mventry[sourceExpr.Source].Value, sourceExpr.Pattern, sourceExpr.Replacement);
                        }
                        else
                        {
                            Tracer.TraceError("attribute-'{0}'-is-not-present-in-metaverse", sourceExpr.Source);
                        }
                        continue;
                    }
                    if (sourceExpression.GetType() == typeof(SourceExpressionAttribute))
                    {
                        SourceExpressionAttribute attr = (SourceExpressionAttribute)sourceExpression;
                        if (mventry[attr.Source].IsPresent)
                        {
                            Tracer.TraceInformation("adding-value-from-MV::'{0}'-to-'{1}'", attr.Source, mventry[attr.Source].Value);
                            concatValue = concatValue + mventry[attr.Source].Value.ToString();
                        }
                        else
                        {
                            Tracer.TraceError("attribute-'{0}'-is-not-present-in-metaverse", attr.Source);
                        }
                        continue;
                    }
                }
                Tracer.TraceInformation("flow-concatenated-attribute-value-'{0}'-to-'{1}'", concatValue, this.Target);
                if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                    csentry.DN = csentry.MA.CreateDN(concatValue);
                else
                {
                    csentry[(this.Target)].Value = concatValue;
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
                Tracer.TraceInformation("exit-attributeflowconcatenate");
            }
        }
    }

    public class AttributeFlowConstant : AttributeFlowBase
    {
        public string EscapedCN;
        public string Constant;

        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowconstant");
            Tracer.Indent();
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
                Tracer.Unindent();
                Tracer.TraceInformation("exit-attributeflowconstant");
            }
        }
    }

    public class AttributeFlowMultivaluedConstant : AttributeFlowBase
    {
        public string EscapedCN;
        [XmlArray("Constants")]
        [XmlArrayItem("Constant")]
        public List<string> Constants;

        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowmultivaluedconstant");
            Tracer.Indent();

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
                Tracer.Unindent();
                Tracer.TraceInformation("exit-attributeflowmutlivaluedconstant");
            }
        }
    }


    public class AttributeFlowGuid : AttributeFlowBase
    {
        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowguid");
            Tracer.Indent();
            base.Generate(ma, csentry, mventry, rule);
            try
            {
                Guid newGuid = Guid.NewGuid();
                Tracer.TraceInformation("new-guid-'{0}'-to-'{1}'", newGuid.ToString(), this.Target);

                if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                    csentry.DN = csentry.MA.CreateDN(newGuid.ToString());
                else
                    csentry[this.Target].Value = newGuid.ToString();
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.Unindent();
                Tracer.TraceInformation("exit-attributeflowguid");
            }
        }
    }

    public class AttributeFlowAttribute : AttributeFlowBase
    {
        public string Prefix;
        public string Source;
        public bool LowercaseTargetValue;
        public bool UppercaseTargetValue;
        public bool TrimTargetValue;

        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowattribute");
            Tracer.Indent();
            base.Generate(ma, csentry, mventry, rule);
            try
            {
                string TargetValue;
                if (Source.Equals("[MVObjectID]"))
                {
                    Tracer.TraceInformation("flow-source: mvobjectid, '{0}'", mventry.ObjectID);
                    if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                    {
                        csentry.DN = csentry.DN = csentry.MA.CreateDN(mventry.ObjectID.ToString());
                    }
                    else
                    {
                        csentry[this.Target].BinaryValue = mventry.ObjectID.ToByteArray();
                    }
                    return;
                }
                else
                {
                    if (mventry[this.Source].DataType == AttributeType.Binary)
                    {
                        TargetValue = BitConverter.ToString(mventry[this.Source].BinaryValue);
                        TargetValue = TargetValue.Replace("-", "");
                    }
                    else
                    {
                        TargetValue = mventry[this.Source].Value;
                    }
                    if (this.LowercaseTargetValue) { TargetValue = TargetValue.ToLower(); }
                    if (this.UppercaseTargetValue) { TargetValue = TargetValue.ToUpper(); }
                    if (this.TrimTargetValue) { TargetValue = TargetValue.Trim(); }

                    TargetValue = (string.IsNullOrEmpty(this.Prefix)) ? TargetValue : this.Prefix + TargetValue;
                    Tracer.TraceInformation("flow-source-value: '{0}'", mventry[this.Source].Value);

                    Tracer.TraceInformation("target-value: '{0}'", TargetValue);
                    if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                    {
                        csentry.DN = csentry.MA.CreateDN(TargetValue);
                    }
                    else
                    {
                        csentry[this.Target].Value = TargetValue;
                    }
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
                Tracer.TraceInformation("exit-attributeflowattribute");
            }
        }
    }

    public class Flow : AttributeFlowBase
    {
        // undocumented for this version. Reserved for future versions 
        // and will replace AttributeFlowBase eventually
    }

    #region source expressions for concatenate
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
}
