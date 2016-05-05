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
        public string Format;

        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowattribute");
            Tracer.Indent();

            bool sourceIsMVObjectID = this.Source.Equals("[mvobjectid]", StringComparison.OrdinalIgnoreCase);
            bool targetIsDN = this.Target.Equals("[dn]", StringComparison.OrdinalIgnoreCase);
            AttributeType sourceType;
            AttributeType targetType;

            if (!sourceIsMVObjectID)
            {
                sourceType = mventry[this.Source].DataType;
            }
            else
            {
                sourceType = AttributeType.String;
            }

            if (!targetIsDN)
            {
                targetType = csentry[this.Target].DataType;
            }
            else
            {
                targetType = AttributeType.String;
            }

            if (sourceIsMVObjectID)
            {
                Tracer.TraceInformation("flow-source-value: '{0}'", mventry.ObjectID.ToString());
                this.FlowMVObjectID(csentry, mventry, targetIsDN, targetType);
                return;
            }
            else
            {
                Tracer.TraceInformation("flow-source-value: '{0}'", mventry[this.Source].Value);
            }

            if (!mventry[this.Source].IsPresent)
            {
                return;
            }

            try
            {
                switch (sourceType)
                {
                    case AttributeType.String:
                        this.FlowStringAttribute(csentry, mventry, targetIsDN, targetType);
                        break;

                    case AttributeType.Integer:
                        this.FlowIntegerAttribute(csentry, mventry, targetIsDN, targetType);
                        break;

                    case AttributeType.Reference:
                        break;

                    case AttributeType.Binary:
                        this.FlowBinaryAttribute(csentry, mventry, targetIsDN, targetType);
                        break;

                    case AttributeType.Boolean:
                        this.FlowBooleanAttribute(csentry, mventry, targetIsDN, targetType);
                        break;

                    case AttributeType.Undefined:
                    default:
                        break;
                }

                if (targetIsDN)
                {
                    Tracer.TraceInformation("target-value: '{0}'", csentry.DN);
                }
                else
                {
                    Tracer.TraceInformation("target-value: '{0}'", csentry[this.Target].Value);
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

        private void FlowStringAttribute(CSEntry csentry, MVEntry mventry, bool targetIsDN, AttributeType targetType)
        {
            string sourceValue = mventry[this.Source].StringValue;

            switch (targetType)
            {
                case AttributeType.String:

                    string target = this.ApplyStringNormalizations(sourceValue);

                    if (targetIsDN)
                    {
                        csentry.DN = csentry.MA.CreateDN(target);
                    }
                    else
                    {
                        csentry[this.Target].StringValue = target;
                    }

                    break;

                case AttributeType.Integer:
                    long intvalue;

                    if (Int64.TryParse(sourceValue, out intvalue))
                    {
                        csentry[this.Target].IntegerValue = intvalue;
                    }
                    else
                    {
                        throw new InvalidCastException(string.Format("The source value '{0}' cannot be converted to a integer value", sourceValue));
                    }
                    break;

                case AttributeType.Binary:
                    if (sourceValue != null)
                    {
                        csentry[this.Target].BinaryValue = System.Text.UTF8Encoding.UTF8.GetBytes(sourceValue);
                    }

                    break;

                case AttributeType.Boolean:
                    if (sourceValue != null)
                    {
                        bool value;

                        if (Boolean.TryParse(sourceValue, out value))
                        {
                            csentry[this.Target].BooleanValue = value;
                        }
                        else
                        {
                            if (sourceValue == "0")
                            {
                                csentry[this.Target].BooleanValue = false;
                            }
                            else if (sourceValue == "1")
                            {
                                csentry[this.Target].BooleanValue = false;
                            }
                            else
                            {
                                throw new InvalidCastException(string.Format("The source value '{0}' cannot be converted to a boolean value", sourceValue));
                            }
                        }
                    }
                    else
                    {
                        csentry[this.Target].BinaryValue = null;
                    }
                    break;

                case AttributeType.Reference:
                case AttributeType.Undefined:
                default:

                    throw new InvalidOperationException(string.Format("Cannot convert string source value to target type {0}", targetType));
            }

        }

        private string ApplyStringNormalizations(string sourceValue)
        {
            string target = sourceValue;

            if (this.LowercaseTargetValue)
            {
                target = target.ToLower();
            }

            if (this.UppercaseTargetValue)
            {
                target = target.ToUpper();
            }

            if (this.TrimTargetValue)
            {
                target = target.Trim();
            }

            return target;
        }

        private void FlowBinaryAttribute(CSEntry csentry, MVEntry mventry, bool targetIsDN, AttributeType targetType)
        {
            if (targetType == AttributeType.Binary)
            {
                csentry[this.Target].BinaryValue = mventry[this.Source].BinaryValue;
            }
            else if (targetType == AttributeType.String)
            {
                string target = Convert.ToBase64String(mventry[this.Source].BinaryValue);

                if (targetIsDN)
                {
                    csentry.DN = csentry.MA.CreateDN(target);
                }
                else
                {
                    csentry[this.Target].StringValue = target;
                }
            }
            else
            {
                throw new InvalidOperationException(string.Format("Cannot convert binary source value to target type {0}", targetType));
            }
        }

        private void FlowBooleanAttribute(CSEntry csentry, MVEntry mventry, bool targetIsDN, AttributeType targetType)
        {
            if (targetType == AttributeType.Boolean)
            {
                csentry[this.Target].BooleanValue = mventry[this.Source].BooleanValue;
            }
            else if (targetType == AttributeType.Integer)
            {
                csentry[this.Target].IntegerValue = mventry[this.Source].BooleanValue ? 1L : 0L;
            }
            else if (targetType == AttributeType.String)
            {
                string target = this.ApplyStringNormalizations(mventry[this.Source].BooleanValue.ToString());
                
                if (targetIsDN)
                {
                    throw new InvalidOperationException("DN cannot be a boolean value");
                }
                else
                {
                    csentry[this.Target].StringValue = target;
                }
            }
            else
            {
                throw new InvalidOperationException(string.Format("Cannot convert boolean source value to target type {0}", targetType));
            }
        }

        private void FlowReferenceAttribute(CSEntry csentry, MVEntry mventry, bool targetIsDN, AttributeType targetType)
        {
            if (targetType == AttributeType.Reference)
            {
                csentry[this.Target].ReferenceValue = mventry[this.Source].ReferenceValue;
            }
            else if (targetType == AttributeType.String)
            {
                csentry[this.Target].StringValue = mventry[this.Source].ReferenceValue.ToString();
            }
            else
            {
                throw new InvalidOperationException(string.Format("Cannot convert reference source value to target type {0}", targetType));
            }
        }



        private void FlowIntegerAttribute(CSEntry csentry, MVEntry mventry, bool targetIsDN, AttributeType targetType)
        {
            long source = mventry[this.Source].IntegerValue;

            if (targetType == AttributeType.Integer)
            {
                csentry[this.Target].IntegerValue = source;
            }
            else if (targetType == AttributeType.String)
            {
                string target = source.ToString();

                if (targetIsDN)
                {
                    csentry.DN = csentry.MA.CreateDN(target);
                }
                else
                {
                    csentry[this.Target].StringValue = target;
                }
            }
            else if (targetType == AttributeType.Boolean)
            {
                if (source == 0)
                {
                    csentry[this.Target].BooleanValue = false;
                }
                else if (source == 1)
                {
                    csentry[this.Target].BooleanValue = true;
                }
                else
                {
                    throw new InvalidOperationException(string.Format("Cannot convert integer source value {0} to boolean", source));
                }
            }
            else
            {
                throw new InvalidOperationException(string.Format("Cannot convert binary source value to target type {0}", targetType));
            }
        }

        private void FlowMVObjectID(CSEntry csentry, MVEntry mventry, bool targetIsDN, AttributeType targetType)
        {
            Tracer.TraceInformation("flow-source: mvobjectid, '{0}'", mventry.ObjectID);

            switch (targetType)
            {
                case AttributeType.String:
                    string target;

                    if (this.Format == null)
                    {
                        target = mventry.ObjectID.ToString();
                    }
                    else
                    {
                        target = mventry.ObjectID.ToString(this.Format);
                    }

                    if (targetIsDN)
                    {
                        csentry.DN = csentry.MA.CreateDN(target);
                    }
                    else
                    {
                        csentry[this.Target].StringValue = target;
                    }

                    break;

                case AttributeType.Binary:
                    csentry[this.Target].BinaryValue = mventry.ObjectID.ToByteArray();
                    break;

                case AttributeType.Integer:
                case AttributeType.Undefined:
                case AttributeType.Boolean:
                case AttributeType.Reference:
                default:
                    throw new InvalidOperationException(string.Format("Cannot flow metaverse object ID to attribute of type {0}", targetType));
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
