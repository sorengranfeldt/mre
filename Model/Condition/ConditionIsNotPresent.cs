namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    public class ConditionIsNotPresent : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttribute].IsPresent)
            {
                return true;
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse attribute value is present) {0}", this.Description);
                return false;
            }
        }
    }

}
