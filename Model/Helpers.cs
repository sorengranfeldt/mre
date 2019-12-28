// october 12, 2015 | soren granfeldt
//	- added for future implementation

using System;

namespace Granfeldt
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml.Serialization;

    [XmlInclude(typeof(HelperValueConstant)), XmlInclude(typeof(HelperValueScopedGuid))]
    public class HelperValue
    {
        [XmlAttribute]
        [XmlTextAttribute()]
        public string Name;
        public virtual string GetValue { get; set; }
        public virtual void Generate()
        {
            Tracer.TraceInformation("helper-value name: {0}, value: {1}", Name, GetValue);
        }
    }
    public class HelperValueScopedGuid : HelperValue
    {
        private Guid guid;
        public override string GetValue
        {
            get
            {
                return this.guid.ToString();
            }
        }
        public override void Generate()
        {
            this.guid = Guid.NewGuid();
            base.Generate();
        }
    }
    public class HelperValueConstant : HelperValue
    {
        public string Value;
        public override string GetValue
        {
            get
            {
                return this.Value;
            }
        }
        public override void Generate()
        {
            base.Generate();
        }
    }

    public class HelperValueRandomPassword : HelperValue
    {
        public int Length = default(int);
        public string Value;
        public override string GetValue
        {
            get
            {
                if (Length == default(int)) Length = 54;
                return RndHelper.CreateRandomPassword(Length);
            }
        }
        public override void Generate()
        {
            base.Generate();
        }
    }
}

internal static class RndHelper
{
    internal static string CreateRandomPassword(int passwordLength = 54)
    {
        string allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ0123456789*&!@$?_-,./?@#()";
        var chars = new char[passwordLength];

        for (int i = 0; i < passwordLength; i++)
        {
            chars[i] = allowedChars[SecureInt(0, allowedChars.Length - 1)];
        }

        return new string(chars);
    }
    private static System.Security.Cryptography.RNGCryptoServiceProvider provider;
    private static int SecureInt()
    {
        if (provider == null) provider = new System.Security.Cryptography.RNGCryptoServiceProvider();
        var byteArray = new byte[4];
        provider.GetBytes(byteArray);

        //convert 4 bytes to an integer
        return BitConverter.ToInt32(byteArray, 0);
    }
    private static int SecureInt(int min, int max)
    {
        if (max < min)
        {
            // Exceptions suck so we'll just swap these values, the range is the same.
            var tmax = min;
            min = max;
            max = tmax;
        }
        int seed = default(int);
        do
        {
            seed = SecureInt();
        } while (seed < min);
        return (seed % (max - min + 1)) + min;
    }
}