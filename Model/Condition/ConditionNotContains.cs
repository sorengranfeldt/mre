namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    public class ConditionNotContains : ConditionContains
    {
        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            return !base.Met(mventry, csentry);
        }
    }


}
