namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Text.RegularExpressions;
    using System.Xml.Serialization;

    public class AttributeFlowConcatenate : AttributeFlowBase
    {
        public SourceExpressionBase[] SourceExpressions;

        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowconcatenate");
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
                Tracer.TraceInformation("exit-attributeflowconcatenate");
            }
        }
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
