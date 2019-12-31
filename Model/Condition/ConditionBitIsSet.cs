namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;

    public class ConditionBitIsSet : ConditionBase
    {
        public string MVAttribute;
        public int BitPosition;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttribute].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: No metaverse value is present for {0}) {1}", this.MVAttribute, this.Description);
                return false;
            }

            bool set = ((mventry[this.MVAttribute].IntegerValue & (1 << this.BitPosition)) != 0);
            if (!set)
            {
                Tracer.TraceInformation("Condition failed (Reason: Bit {0} in attribute {1} is not set) {2} ", this.BitPosition, this.MVAttribute, this.Description);
                return false;
            }
            return true;
        }
    }
}
