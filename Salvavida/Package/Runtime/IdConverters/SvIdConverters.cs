namespace Salvavida
{
    public class SvIdConverterString : ISvIdConverter<string>
    {
        public string ConvertFrom(string str)
        {
            return str;
        }

        public string ConvertTo(string value)
        {
            return value;
        }
    }

    public class SvIdConverterInt : ISvIdConverter<int>
    {
        public int ConvertFrom(string str)
        {
            return int.Parse(str);
        }

        public string ConvertTo(int value)
        {
            return value.ToString();
        }
    }
}
