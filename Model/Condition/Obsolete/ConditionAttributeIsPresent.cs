﻿namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;

    [Obsolete]
    public class ConditionAttributeIsPresent : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            Tracer.TraceWarning("Condition type 'ConditionAttributeIsPresent' is obsolete. Please see documentation.");
            if (mventry[this.MVAttribute].IsPresent)
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
