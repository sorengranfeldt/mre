namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
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
                case AttributeType.Boolean:
                case AttributeType.Reference:
                default:
                    throw new InvalidOperationException(string.Format("Cannot flow metaverse object ID to attribute of type {0}", targetType));
            }
        }
    }
}
