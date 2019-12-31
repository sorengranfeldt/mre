namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;

    public class ConditionIsDNNotEqual : ConditionBase
    {
        public string MVAttribute;
        public string CSAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            return !ConditionIsDNEqual.Compare(mventry, csentry, this.CSAttribute, this.MVAttribute);
        }
    }

}
