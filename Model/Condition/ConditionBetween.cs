namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;

    public class ConditionBetween : ConditionBase
    {
        public string MVAttributeStartDate;
        public string MVAttributeEndDate;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttributeStartDate].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: MVAttributeStartDate {0} attribute value is not present) / {0}", this.MVAttributeStartDate, this.Description);
                return false;
            }
            if (!mventry[this.MVAttributeEndDate].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: MVAttributeEndDate {0} attribute value is not present) / {0}", this.MVAttributeEndDate, this.Description);
                return false;
            }

            DateTime startDate;
            DateTime endDate;
            if (!DateTime.TryParse(mventry[this.MVAttributeStartDate].StringValue, out startDate))
            {
                Tracer.TraceWarning("unable-to-parse-start-mvvalue-to-datetime {0}", mventry[this.MVAttributeStartDate].StringValue);
                return false;
            }
            if (!DateTime.TryParse(mventry[this.MVAttributeEndDate].StringValue, out endDate))
            {
                Tracer.TraceWarning("unable-to-parse-end-mvvalue-to-datetime {0}", mventry[this.MVAttributeEndDate].StringValue);
                return false;
            }

            DateTime now = DateTime.Now;
            bool returnValue = (startDate < now) && (now < endDate);
            Tracer.TraceInformation("compare-dates now: {0}, start: {1}, end: {2}, is-between: {3}", now, startDate, endDate, returnValue);
            return returnValue;
        }
    }
}
