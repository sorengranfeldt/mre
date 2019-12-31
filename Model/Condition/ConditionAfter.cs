namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;

    public class ConditionAfter : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (mventry[this.MVAttribute].IsPresent)
            {
                DateTime value;
                if (DateTime.TryParse(mventry[this.MVAttribute].StringValue, out value))
                {
                    DateTime now = DateTime.Now;
                    bool returnValue = now > value;
                    Tracer.TraceInformation("compare-dates now: {0}, mvvalue: {1}, is-after: {2}", now, value, returnValue);
                    return returnValue;
                }
                else
                {
                    Tracer.TraceWarning("unable-to-parse-mvvalue-to-datetime {0}", mventry[this.MVAttribute].StringValue);
                    return false;
                }
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse attribute value is not present) {0}", this.Description);
                return false;
            }
        }
    }
}
