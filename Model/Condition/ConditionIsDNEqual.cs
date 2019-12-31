namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    public class ConditionIsDNEqual : ConditionBase
    {
        public string MVAttribute;
        public string CSAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            return ConditionIsDNEqual.Compare(mventry, csentry, this.CSAttribute, this.MVAttribute);
        }
        internal static bool Compare(MVEntry mventry, CSEntry csentry, string csattributeName, string mvattributeName)
        {
            ReferenceValue csval = null;
            ReferenceValue mvval = null;

            Attrib mvAttrib = mventry[mvattributeName];

            if (csattributeName == "[DN]")
            {
                csval = csentry.DN;
            }
            else
            {
                Attrib csAttrib = csentry[csattributeName];

                if (!csAttrib.IsPresent && !mvAttrib.IsPresent)
                {
                    return true;
                }

                if (csAttrib.IsPresent ^ mvAttrib.IsPresent)
                {
                    return false;
                }

                switch (csAttrib.DataType)
                {
                    case AttributeType.Reference:
                        csval = csAttrib.ReferenceValue;
                        break;

                    case AttributeType.String:
                        csval = csentry.MA.CreateDN(csAttrib.StringValue);
                        break;

                    default:
                        Tracer.TraceError("Can only compare string values as DNs");
                        return false;
                }
            }

            if (mvAttrib.DataType != AttributeType.String)
            {
                Tracer.TraceError("Can only compare string values as DNs");
            }

            mvval = csentry.MA.CreateDN(mvAttrib.StringValue);

            return mvval.Equals(csval);
        }
    }

}
