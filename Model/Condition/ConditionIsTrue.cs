namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;

    public class ConditionIsTrue : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttribute].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: No metaverse value is present) {0}", this.Description);
                return false;
            }

            if (!mventry[this.MVAttribute].BooleanValue)
            {
                Tracer.TraceInformation("Condition failed (Reason: Boolean value is false) {0}", this.Description);
                return false;
            }
            return true;
        }
    }
}
