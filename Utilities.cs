using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.MetadirectoryServices;

namespace Granfeldt
{
    static class StringHelpers
    {
        public static string ReplaceWithMVValueOrBlank(this string source, MVEntry mventry)
        {
            return ReplaceWithMVValueOrBlank(source, mventry, "");
        }
        public static string ReplaceWithHelperValuesOrBlank(this string source, IList<HelperValue> helpers)
        {
            if (helpers != null)
            {
                MatchCollection mc = Regex.Matches(source, @"(?<=#helper\:)(?<helpername>\w+)#", RegexOptions.Compiled);
                foreach (Match match in mc)
                {
                    string matchValue = match.Value.Trim('#');
                    string newValue = helpers.FirstOrDefault(x => x.Name.Equals(matchValue, StringComparison.OrdinalIgnoreCase)).GetValue;
                    Trace.TraceInformation("replaced-helper-value-'{0}'-with-'{1}'", matchValue, newValue);
                    source = Regex.Replace(source, string.Format(@"#helper\:{0}", match.Value), newValue);
                }
            }
            return source;
        }

        public static string ReplaceWithMVValueOrBlank(this string source, MVEntry mventry, string escapedCN)
        {
            source = Regex.Replace(source, @"#param:EscapedCN#", escapedCN ?? "", RegexOptions.IgnoreCase);
            MatchCollection mc = Regex.Matches(source, @"(?<=#mv\:)(?<attrname>\w+)#", RegexOptions.Compiled);
            foreach (Match match in mc)
            {
                string matchValue = match.Value.Trim('#');
                string newValue = mventry[matchValue].IsPresent ? mventry[matchValue].Value : "";
                Trace.TraceInformation("replaced-'{0}'-with-'{1}'", matchValue, newValue);
                source = Regex.Replace(source, string.Format(@"#mv\:{0}", match.Value), mventry[matchValue].Value);
            }
            return source;
        }
    }
}
