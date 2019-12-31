namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Collections.Generic;
    using System.Xml.Serialization;

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
            Tracer.TraceInformation("enter-subconditionsmet");
            try
            {
                if (Operator.Equals(ConditionOperator.And))
                {
                    bool met = true;
                    foreach (ConditionBase condition in ConditionBase)
                    {
                        met = condition.Met(mventry, csentry);
                        Tracer.TraceInformation("'And' condition '{0}'/{1} returned: {2}", condition.GetType().Name, condition.Description, met);
                        if (met == false) break;
                    }
                    Tracer.TraceInformation("All 'And' conditions {0} met", met ? "were" : "were not");
                    return met;
                }
                else
                {
                    bool met = false;
                    foreach (ConditionBase condition in ConditionBase)
                    {
                        met = condition.Met(mventry, csentry);
                        Tracer.TraceInformation("'Or' condition '{0}'/{1} returned: {2}", condition.GetType().Name, condition.Description, met);
                        if (met == true) break;
                    }
                    Tracer.TraceInformation("One or more 'Or' conditions {0} met", met ? "were" : "were not");
                    return met;
                }
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.TraceInformation("exit-subconditionsmet");
            }
        }
    }
}
