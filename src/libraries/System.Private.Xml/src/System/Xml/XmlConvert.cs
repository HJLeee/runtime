// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Globalization;
using System.Xml.Schema;
using System.Diagnostics;
using System.Collections;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace System.Xml
{
    // ExceptionType enum is used inside XmlConvert to specify which type of exception should be thrown at some of the verification and exception creating methods
    internal enum ExceptionType
    {
        ArgumentException,
        XmlException,
    }

    // Options for serializing and deserializing DateTime
    public enum XmlDateTimeSerializationMode
    {
        Local,
        Utc,
        Unspecified,
        RoundtripKind,
    }

    /// <devdoc>
    ///    Encodes and decodes XML names according to
    ///    the "Encoding of arbitrary Unicode Characters in XML Names" specification.
    /// </devdoc>
    public partial class XmlConvert
    {
        internal static char[] crt = new char[] { '\n', '\r', '\t' };

        /// <devdoc>
        ///    <para>
        ///       Converts names, such
        ///       as DataTable or
        ///       DataColumn names, that contain characters that are not permitted in
        ///       XML names to valid names.</para>
        /// </devdoc>
        [return: NotNullIfNotNull("name")]
        public static string? EncodeName(string? name)
        {
            return EncodeName(name, true/*Name_not_NmToken*/, false/*Local?*/);
        }

        /// <devdoc>
        ///    <para> Verifies the name is valid
        ///       according to production [7] in the XML spec.</para>
        /// </devdoc>
        [return: NotNullIfNotNull("name")]
        public static string? EncodeNmToken(string? name)
        {
            return EncodeName(name, false/*Name_not_NmToken*/, false/*Local?*/);
        }

        /// <devdoc>
        ///    <para>Converts names, such as DataTable or DataColumn names, that contain
        ///       characters that are not permitted in XML names to valid names.</para>
        /// </devdoc>
        [return: NotNullIfNotNull("name")]
        public static string? EncodeLocalName(string? name)
        {
            return EncodeName(name, true/*Name_not_NmToken*/, true/*Local?*/);
        }

        /// <devdoc>
        ///    <para>
        ///       Transforms an XML name into an object name (such as DataTable or DataColumn).</para>
        /// </devdoc>
        [return: NotNullIfNotNull("name")]
        public static string? DecodeName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            int underscorePos = name.IndexOf('_');
            if (underscorePos < 0)
            {
                return name;
            }

            Regex.ValueMatchEnumerator en = DecodeCharRegex().EnumerateMatches(name.AsSpan(underscorePos));
            int matchPos = -1;
            if (en.MoveNext())
            {
                matchPos = underscorePos + en.Current.Index;
            }

            StringBuilder? bufBld = null;
            int length = name.Length;
            int copyPosition = 0;
            for (int position = 0; position < length - EncodedCharLength + 1; position++)
            {
                if (position == matchPos)
                {
                    if (en.MoveNext())
                    {
                        matchPos = underscorePos + en.Current.Index;
                    }

                    bufBld ??= new StringBuilder(length + 20);
                    bufBld.Append(name, copyPosition, position - copyPosition);

                    if (name[position + 6] != '_')
                    { //_x1234_
                        int u =
                            FromHex(name[position + 2]) * 0x10000000 +
                            FromHex(name[position + 3]) * 0x1000000 +
                            FromHex(name[position + 4]) * 0x100000 +
                            FromHex(name[position + 5]) * 0x10000 +

                            FromHex(name[position + 6]) * 0x1000 +
                            FromHex(name[position + 7]) * 0x100 +
                            FromHex(name[position + 8]) * 0x10 +
                            FromHex(name[position + 9]);

                        if (u >= 0x00010000)
                        {
                            if (u <= 0x0010ffff)
                            { //convert to two chars
                                copyPosition = position + EncodedCharLength + 4;
                                char lowChar, highChar;
                                XmlCharType.SplitSurrogateChar(u, out lowChar, out highChar);
                                bufBld.Append(highChar);
                                bufBld.Append(lowChar);
                            }
                            //else bad ucs-4 char don't convert
                        }
                        else
                        { //convert to single char
                            copyPosition = position + EncodedCharLength + 4;
                            bufBld.Append((char)u);
                        }
                        position += EncodedCharLength - 1 + 4; //just skip
                    }
                    else
                    {
                        copyPosition = position + EncodedCharLength;
                        bufBld.Append((char)(
                            FromHex(name[position + 2]) * 0x1000 +
                            FromHex(name[position + 3]) * 0x100 +
                            FromHex(name[position + 4]) * 0x10 +
                            FromHex(name[position + 5])));
                        position += EncodedCharLength - 1;
                    }
                }
            }
            if (copyPosition == 0)
            {
                return name;
            }
            else
            {
                if (copyPosition < length)
                {
                    bufBld!.Append(name, copyPosition, length - copyPosition);
                }

                return bufBld!.ToString();
            }
        }

        [return: NotNullIfNotNull("name")]
        private static string? EncodeName(string? name, /*Name_not_NmToken*/ bool first, bool local)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            StringBuilder? bufBld = null;
            int length = name.Length;
            int copyPosition = 0;
            int position = 0;

            int underscorePos = name.IndexOf('_');
            MatchCollection? mc;
            IEnumerator? en = null;
            if (underscorePos >= 0)
            {
                mc = EncodeCharRegex().Matches(name, underscorePos);
                en = mc.GetEnumerator();
            }

            int matchPos = -1;
            if (en != null && en.MoveNext())
            {
                Match m = (Match)en.Current!;
                matchPos = m.Index - 1;
            }

            if (first)
            {
                if ((!XmlCharType.IsStartNCNameCharXml4e(name[0]) && (local || name[0] != ':')) ||
                     matchPos == 0)
                {
                    bufBld ??= new StringBuilder(length + 20);

                    bufBld.Append("_x");
                    if (length > 1 && XmlCharType.IsHighSurrogate(name[0]) && XmlCharType.IsLowSurrogate(name[1]))
                    {
                        int x = name[0];
                        int y = name[1];
                        int u = XmlCharType.CombineSurrogateChar(y, x);
                        bufBld.Append($"{u:X8}");
                        position++;
                        copyPosition = 2;
                    }
                    else
                    {
                        bufBld.Append($"{(int)name[0]:X4}");
                        copyPosition = 1;
                    }

                    bufBld.Append('_');
                    position++;

                    if (matchPos == 0)
                        if (en!.MoveNext())
                        {
                            Match m = (Match)en.Current!;
                            matchPos = m.Index - 1;
                        }
                }
            }
            for (; position < length; position++)
            {
                if ((local && !XmlCharType.IsNCNameCharXml4e(name[position])) ||
                    (!local && !XmlCharType.IsNameCharXml4e(name[position])) ||
                    (matchPos == position))
                {
                    if (bufBld == null)
                    {
                        bufBld = new StringBuilder(length + 20);
                    }
                    if (matchPos == position)
                        if (en!.MoveNext())
                        {
                            Match m = (Match)en.Current!;
                            matchPos = m.Index - 1;
                        }

                    bufBld.Append(name, copyPosition, position - copyPosition);
                    bufBld.Append("_x");
                    if ((length > position + 1) && XmlCharType.IsHighSurrogate(name[position]) && XmlCharType.IsLowSurrogate(name[position + 1]))
                    {
                        int x = name[position];
                        int y = name[position + 1];
                        int u = XmlCharType.CombineSurrogateChar(y, x);
                        bufBld.Append($"{u:X8}");
                        copyPosition = position + 2;
                        position++;
                    }
                    else
                    {
                        bufBld.Append($"{(int)name[position]:X4}");
                        copyPosition = position + 1;
                    }
                    bufBld.Append('_');
                }
            }
            if (copyPosition == 0)
            {
                return name;
            }
            else
            {
                if (copyPosition < length)
                {
                    bufBld!.Append(name, copyPosition, length - copyPosition);
                }

                return bufBld!.ToString();
            }
        }

        private const int EncodedCharLength = 7; // ("_xFFFF_".Length);

        [RegexGenerator("_[Xx][0-9a-fA-F]{4}(?:_|[0-9a-fA-F]{4}_)")]
        private static partial Regex DecodeCharRegex();

        [RegexGenerator("(?<=_)[Xx][0-9a-fA-F]{4}(?:_|[0-9a-fA-F]{4}_)")]
        private static partial Regex EncodeCharRegex();

        private static int FromHex(char digit)
        {
            return HexConverter.FromChar(digit);
        }

        internal static byte[] FromBinHexString(string s)
        {
            return FromBinHexString(s, true);
        }

        internal static byte[] FromBinHexString(string s, bool allowOddCount)
        {
            ArgumentNullException.ThrowIfNull(s);

            return BinHexDecoder.Decode(s.AsSpan(), allowOddCount);
        }

        internal static string ToBinHexString(byte[] inArray)
        {
            ArgumentNullException.ThrowIfNull(inArray);

            return BinHexEncoder.Encode(inArray, 0, inArray.Length);
        }

        //
        // Verification methods for strings
        /// <devdoc>
        ///    <para>
        ///    </para>
        /// </devdoc>
        public static string VerifyName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            if (name.Length == 0)
            {
                throw new ArgumentNullException(nameof(name), SR.Xml_EmptyName);
            }

            // parse name
            int endPos = ValidateNames.ParseNameNoNamespaces(name, 0);

            if (endPos != name.Length)
            {
                // did not parse to the end -> there is invalid character at endPos
                throw CreateInvalidNameCharException(name, endPos, ExceptionType.XmlException);
            }

            return name;
        }


        internal static Exception? TryVerifyName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return new XmlException(SR.Xml_EmptyName, string.Empty);
            }

            int endPos = ValidateNames.ParseNameNoNamespaces(name, 0);
            if (endPos != name.Length)
            {
                return new XmlException(endPos == 0 ? SR.Xml_BadStartNameChar : SR.Xml_BadNameChar, XmlException.BuildCharExceptionArgs(name, endPos));
            }

            return null;
        }

        internal static string VerifyQName(string name, ExceptionType exceptionType)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            int endPos = ValidateNames.ParseQName(name, 0, out _);
            if (endPos != name.Length)
            {
                throw CreateException(SR.Xml_BadNameChar, XmlException.BuildCharExceptionArgs(name, endPos), exceptionType, 0, endPos + 1);
            }

            return name;
        }

        /// <devdoc>
        ///    <para>
        ///    </para>
        /// </devdoc>
        public static string VerifyNCName(string name)
        {
            return VerifyNCName(name, ExceptionType.XmlException);
        }

        internal static string VerifyNCName(string name, ExceptionType exceptionType)
        {
            ArgumentNullException.ThrowIfNull(name);

            if (name.Length == 0)
            {
                throw new ArgumentNullException(nameof(name), SR.Xml_EmptyLocalName);
            }

            int end = ValidateNames.ParseNCName(name, 0);

            if (end != name.Length)
            {
                // If the string is not a valid NCName, then throw or return false
                throw CreateInvalidNameCharException(name, end, exceptionType);
            }

            return name;
        }

        internal static Exception? TryVerifyNCName(string name)
        {
            int len = ValidateNames.ParseNCName(name);

            if (len == 0 || len != name.Length)
            {
                return ValidateNames.GetInvalidNameException(name, 0, len);
            }

            return null;
        }

        /// <devdoc>
        ///    <para>
        ///    </para>
        /// </devdoc>
        [return: NotNullIfNotNull("token")]
        public static string? VerifyTOKEN(string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return token;
            }

            if (token.StartsWith(' ') ||
                token.EndsWith(' ') ||
                token.IndexOfAny(crt) >= 0 ||
                token.Contains("  "))
            {
                throw new XmlException(SR.Sch_NotTokenString, token);
            }
            return token;
        }

        internal static Exception? TryVerifyTOKEN(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            if (token.StartsWith(' ') ||
                token.EndsWith(' ') ||
                token.IndexOfAny(crt) >= 0 ||
                token.Contains("  "))
            {
                return new XmlException(SR.Sch_NotTokenString, token);
            }

            return null;
        }

        /// <devdoc>
        ///    <para>
        ///    </para>
        /// </devdoc>
        public static string VerifyNMTOKEN(string name)
        {
            return VerifyNMTOKEN(name, ExceptionType.XmlException);
        }

        internal static string VerifyNMTOKEN(string name, ExceptionType exceptionType)
        {
            ArgumentNullException.ThrowIfNull(name);

            if (name.Length == 0)
            {
                throw CreateException(SR.Xml_InvalidNmToken, name, exceptionType);
            }

            int endPos = ValidateNames.ParseNmtokenNoNamespaces(name, 0);

            if (endPos != name.Length)
            {
                throw CreateException(SR.Xml_BadNameChar, XmlException.BuildCharExceptionArgs(name, endPos), exceptionType, 0, endPos + 1);
            }

            return name;
        }

        internal static Exception? TryVerifyNMTOKEN(string name)
        {
            if (name == null || name.Length == 0)
            {
                return new XmlException(SR.Xml_EmptyName, string.Empty);
            }

            int endPos = ValidateNames.ParseNmtokenNoNamespaces(name, 0);
            if (endPos != name.Length)
            {
                return new XmlException(SR.Xml_BadNameChar, XmlException.BuildCharExceptionArgs(name, endPos));
            }

            return null;
        }

        internal static Exception? TryVerifyNormalizedString(string str)
        {
            if (str.IndexOfAny(crt) != -1)
            {
                return new XmlSchemaException(SR.Sch_NotNormalizedString, str);
            }

            return null;
        }

        // Verification method for XML characters as defined in XML spec production [2] Char.
        // Throws XmlException if invalid character is found, otherwise returns the input string.
        public static string VerifyXmlChars(string content)
        {
            ArgumentNullException.ThrowIfNull(content);

            VerifyCharData(content, ExceptionType.XmlException);
            return content;
        }

        // Verification method for XML public ID characters as defined in XML spec production [13] PubidChar.
        // Throws XmlException if invalid character is found, otherwise returns the input string.
        public static string VerifyPublicId(string publicId)
        {
            ArgumentNullException.ThrowIfNull(publicId);

            // returns the position of invalid character or -1
            int pos = XmlCharType.IsPublicId(publicId);
            if (pos != -1)
            {
                throw CreateInvalidCharException(publicId, pos, ExceptionType.XmlException);
            }

            return publicId;
        }

        // Verification method for XML whitespace characters as defined in XML spec production [3] S.
        // Throws XmlException if invalid character is found, otherwise returns the input string.
        public static string VerifyWhitespace(string content)
        {
            ArgumentNullException.ThrowIfNull(content);

            // returns the position of invalid character or -1
            int pos = XmlCharType.IsOnlyWhitespaceWithPos(content);
            if (pos != -1)
            {
                throw new XmlException(SR.Xml_InvalidWhitespaceCharacter, XmlException.BuildCharExceptionArgs(content, pos), 0, pos + 1);
            }

            return content;
        }

        //
        // Verification methods for single characters and surrogates
        //
        // In cases where the direct call into XmlCharType would not get automatically inlined (because of the use of byte* field),
        // direct access to the XmlCharType.charProperties is used instead (= manual inlining).
        //

        // Start name character types - as defined in Namespaces XML 1.0 spec (second edition) production [6] NCNameStartChar
        //                              combined with the production [4] NameStartChar of XML 1.0 spec
        public static bool IsStartNCNameChar(char ch)
        {
            return XmlCharType.IsStartNCNameSingleChar(ch);
        }

        // Name character types - as defined in Namespaces XML 1.0 spec (second edition) production [6] NCNameStartChar
        //                        combined with the production [4] NameChar of XML 1.0 spec
        public static bool IsNCNameChar(char ch)
        {
            return XmlCharType.IsNCNameSingleChar(ch);
        }

        // Valid XML character - as defined in XML 1.0 spec (fifth edition) production [2] Char
        public static bool IsXmlChar(char ch)
        {
            return XmlCharType.IsCharData(ch);
        }

        public static bool IsXmlSurrogatePair(char lowChar, char highChar)
        {
            return XmlCharType.IsHighSurrogate(highChar) && XmlCharType.IsLowSurrogate(lowChar);
        }

        // Valid PUBLIC ID character - as defined in XML 1.0 spec (fifth edition) production [13] PublidChar
        public static bool IsPublicIdChar(char ch)
        {
            return XmlCharType.IsPubidChar(ch);
        }

        // Valid Xml whitespace - as defined in XML 1.0 spec (fifth edition) production [3] S
        public static bool IsWhitespaceChar(char ch)
        {
            return XmlCharType.IsWhiteSpace(ch);
        }

        // Value convertors:
        //
        // String representation of Base types in XML (xsd) sometimes differ from
        // one common language runtime offer and for all types it has to be locale independent.
        // o -- means that XmlConvert pass through to common language runtime converter with InvariantInfo FormatInfo
        // x -- means we doing something special to make a conversion.
        //
        // From:  To: Bol Chr SBy Byt I16 U16 I32 U32 I64 U64 Sgl Dbl Dec Dat Tim Str uid
        // ------------------------------------------------------------------------------
        // Boolean                                                                 x
        // Char                                                                    o
        // SByte                                                                   o
        // Byte                                                                    o
        // Int16                                                                   o
        // UInt16                                                                  o
        // Int32                                                                   o
        // UInt32                                                                  o
        // Int64                                                                   o
        // UInt64                                                                  o
        // Single                                                                  x
        // Double                                                                  x
        // Decimal                                                                 o
        // DateTime                                                                x
        // String      x   o   o   o   o   o   o   o   o   o   o   x   x   o   o       x
        // Guid                                                                    x
        // -----------------------------------------------------------------------------

        public static string ToString(bool value)
        {
            return value ? "true" : "false";
        }

        public static string ToString(char value)
        {
            return value.ToString();
        }

        public static string ToString(decimal value)
        {
            return value.ToString(null, NumberFormatInfo.InvariantInfo);
        }

        [CLSCompliant(false)]
        public static string ToString(sbyte value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        public static string ToString(short value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        public static string ToString(int value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        public static string ToString(long value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        public static string ToString(byte value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        [CLSCompliant(false)]
        public static string ToString(ushort value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        [CLSCompliant(false)]
        public static string ToString(uint value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        [CLSCompliant(false)]
        public static string ToString(ulong value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        public static string ToString(float value)
        {
            if (float.IsNegativeInfinity(value)) return "-INF";
            if (float.IsPositiveInfinity(value)) return "INF";
            if (IsNegativeZero((double)value))
            {
                return ("-0");
            }
            return value.ToString("R", NumberFormatInfo.InvariantInfo);
        }

        public static string ToString(double value)
        {
            if (double.IsNegativeInfinity(value)) return "-INF";
            if (double.IsPositiveInfinity(value)) return "INF";
            if (IsNegativeZero(value))
            {
                return ("-0");
            }

            return value.ToString("R", NumberFormatInfo.InvariantInfo);
        }

        public static string ToString(TimeSpan value)
        {
            return new XsdDuration(value).ToString();
        }

        [Obsolete("Use XmlConvert.ToString() that accepts an XmlDateTimeSerializationMode instead.")]
        public static string ToString(DateTime value)
        {
            return ToString(value, "yyyy-MM-ddTHH:mm:ss.fffffffzzzzzz");
        }

        public static string ToString(DateTime value, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format)
        {
            return value.ToString(format, DateTimeFormatInfo.InvariantInfo);
        }

        public static string ToString(DateTime value, XmlDateTimeSerializationMode dateTimeOption)
        {
            switch (dateTimeOption)
            {
                case XmlDateTimeSerializationMode.Local:
                    value = SwitchToLocalTime(value);
                    break;

                case XmlDateTimeSerializationMode.Utc:
                    value = SwitchToUtcTime(value);
                    break;

                case XmlDateTimeSerializationMode.Unspecified:
                    value = new DateTime(value.Ticks, DateTimeKind.Unspecified);
                    break;

                case XmlDateTimeSerializationMode.RoundtripKind:
                    break;

                default:
                    throw new ArgumentException(SR.Format(SR.Sch_InvalidDateTimeOption, dateTimeOption, nameof(dateTimeOption)));
            }

            XsdDateTime xsdDateTime = new XsdDateTime(value, XsdDateTimeFlags.DateTime);
            return xsdDateTime.ToString();
        }

        public static string ToString(DateTimeOffset value)
        {
            XsdDateTime xsdDateTime = new XsdDateTime(value);
            return xsdDateTime.ToString();
        }

        public static string ToString(DateTimeOffset value, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format)
        {
            return value.ToString(format, DateTimeFormatInfo.InvariantInfo);
        }

        public static string ToString(Guid value)
        {
            return value.ToString();
        }

        public static bool ToBoolean(string s)
        {
            s = TrimString(s);
            if (s == "1" || s == "true") return true;
            if (s == "0" || s == "false") return false;
            throw new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Boolean"));
        }

        internal static Exception? TryToBoolean(string s, out bool result)
        {
            s = TrimString(s);
            if (s == "0" || s == "false")
            {
                result = false;
                return null;
            }
            else if (s == "1" || s == "true")
            {
                result = true;
                return null;
            }

            result = false;
            return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Boolean"));
        }

        public static char ToChar(string s)
        {
            ArgumentNullException.ThrowIfNull(s);

            if (s.Length != 1)
            {
                throw new FormatException(SR.XmlConvert_NotOneCharString);
            }

            return s[0];
        }

        internal static Exception? TryToChar(string s, out char result)
        {
            if (!char.TryParse(s, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Char"));
            }

            return null;
        }

        public static decimal ToDecimal(string s)
        {
            return decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToDecimal(string s, out decimal result)
        {
            if (!decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Decimal"));
            }

            return null;
        }

        internal static decimal ToInteger(string s)
        {
            return decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToInteger(string s, out decimal result)
        {
            if (!decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Integer"));
            }

            return null;
        }

        [CLSCompliant(false)]
        public static sbyte ToSByte(string s)
        {
            return sbyte.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToSByte(string s, out sbyte result)
        {
            if (!sbyte.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "SByte"));
            }

            return null;
        }

        public static short ToInt16(string s)
        {
            return short.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToInt16(string s, out short result)
        {
            if (!short.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Int16"));
            }

            return null;
        }

        public static int ToInt32(string s)
        {
            return int.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToInt32(string s, out int result)
        {
            if (!int.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Int32"));
            }

            return null;
        }

        public static long ToInt64(string s)
        {
            return long.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToInt64(string s, out long result)
        {
            if (!long.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Int64"));
            }

            return null;
        }

        public static byte ToByte(string s)
        {
            return byte.Parse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToByte(string s, out byte result)
        {
            if (!byte.TryParse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Byte"));
            }

            return null;
        }

        [CLSCompliant(false)]
        public static ushort ToUInt16(string s)
        {
            return ushort.Parse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToUInt16(string s, out ushort result)
        {
            if (!ushort.TryParse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "UInt16"));
            }

            return null;
        }

        [CLSCompliant(false)]
        public static uint ToUInt32(string s)
        {
            return uint.Parse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToUInt32(string s, out uint result)
        {
            if (!uint.TryParse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "UInt32"));
            }

            return null;
        }

        [CLSCompliant(false)]
        public static ulong ToUInt64(string s)
        {
            return ulong.Parse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
        }

        internal static Exception? TryToUInt64(string s, out ulong result)
        {
            if (!ulong.TryParse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "UInt64"));
            }

            return null;
        }

        public static float ToSingle(string s)
        {
            s = TrimString(s);
            if (s == "-INF") return float.NegativeInfinity;
            if (s == "INF") return float.PositiveInfinity;
            float f = float.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, NumberFormatInfo.InvariantInfo);
            if (f == 0 && s[0] == '-')
            {
                return -0f;
            }

            return f;
        }

        internal static Exception? TryToSingle(string s, out float result)
        {
            s = TrimString(s);
            if (s == "-INF")
            {
                result = float.NegativeInfinity;
                return null;
            }
            else if (s == "INF")
            {
                result = float.PositiveInfinity;
                return null;
            }
            else if (!float.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Single"));
            }

            if (result == 0 && s[0] == '-')
            {
                result = -0f;
            }

            return null;
        }

        public static double ToDouble(string s)
        {
            s = TrimString(s);
            if (s == "-INF") return double.NegativeInfinity;
            if (s == "INF") return double.PositiveInfinity;

            double dVal = double.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo);
            if (dVal == 0 && s[0] == '-')
            {
                return -0d;
            }

            return dVal;
        }

        internal static Exception? TryToDouble(string s, out double result)
        {
            s = TrimString(s);
            if (s == "-INF")
            {
                result = double.NegativeInfinity;
                return null;
            }
            else if (s == "INF")
            {
                result = double.PositiveInfinity;
                return null;
            }
            else if (!double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, NumberFormatInfo.InvariantInfo, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Double"));
            }

            if (result == 0 && s[0] == '-')
            {
                result = -0d;
            }

            return null;
        }

        internal static double ToXPathDouble(object? o)
        {
            if (o is string str)
            {
                str = TrimString(str);
                if (str.Length != 0 && str[0] != '+')
                {
                    double d;
                    if (double.TryParse(str, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowTrailingWhite, NumberFormatInfo.InvariantInfo, out d))
                    {
                        return d;
                    }
                }
                return double.NaN;
            }

            if (o is double oDouble)
            {
                return oDouble;
            }

            if (o is bool oBool)
            {
                return oBool ? 1.0 : 0.0;
            }

            try
            {
                return Convert.ToDouble(o, NumberFormatInfo.InvariantInfo);
            }
            catch (FormatException)
            {
            }
            catch (OverflowException)
            {
            }
            catch (ArgumentNullException) { }

            return double.NaN;
        }

        internal static string? ToXPathString(object? value)
        {
            if (value is string s)
            {
                return s;
            }
            else if (value is double)
            {
                return ((double)value).ToString("R", NumberFormatInfo.InvariantInfo);
            }
            else if (value is bool)
            {
                return (bool)value ? "true" : "false";
            }
            else
            {
                return Convert.ToString(value, NumberFormatInfo.InvariantInfo);
            }
        }

        internal static double XPathRound(double value)
        {
            double temp = Math.Round(value);
            return (value - temp == 0.5) ? temp + 1 : temp;
        }

        public static TimeSpan ToTimeSpan(string s)
        {
            XsdDuration duration;
            TimeSpan timeSpan;

            try
            {
                duration = new XsdDuration(s);
            }
            catch (Exception)
            {
                // Remap exception for v1 compatibility
                throw new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "TimeSpan"));
            }

            timeSpan = duration.ToTimeSpan();

            return timeSpan;
        }

        internal static Exception? TryToTimeSpan(string s, out TimeSpan result)
        {
            XsdDuration duration;
            Exception? exception;

            exception = XsdDuration.TryParse(s, out duration);
            if (exception != null)
            {
                result = TimeSpan.MinValue;
                return exception;
            }
            else
            {
                return duration.TryToTimeSpan(out result);
            }
        }

        // use AllDateTimeFormats property to access the formats
        private static volatile string[]? s_allDateTimeFormats;

        // NOTE: Do not use this property for reference comparison. It may not be unique.
        private static string[] AllDateTimeFormats
        {
            get
            {
                if (s_allDateTimeFormats == null)
                {
                    CreateAllDateTimeFormats();
                }

                return s_allDateTimeFormats!;
            }
        }

        private static void CreateAllDateTimeFormats()
        {
            if (s_allDateTimeFormats == null)
            {
                // no locking; the array is immutable so it's not a problem that it may get initialized more than once
                s_allDateTimeFormats = new string[] {
                    "yyyy-MM-ddTHH:mm:ss.FFFFFFFzzzzzz", //dateTime
                    "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
                    "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ",
                    "HH:mm:ss.FFFFFFF",                  //time
                    "HH:mm:ss.FFFFFFFZ",
                    "HH:mm:ss.FFFFFFFzzzzzz",
                    "yyyy-MM-dd",                   // date
                    "yyyy-MM-ddZ",
                    "yyyy-MM-ddzzzzzz",
                    "yyyy-MM",                      // yearMonth
                    "yyyy-MMZ",
                    "yyyy-MMzzzzzz",
                    "yyyy",                         // year
                    "yyyyZ",
                    "yyyyzzzzzz",
                    "--MM-dd",                      // monthDay
                    "--MM-ddZ",
                    "--MM-ddzzzzzz",
                    "---dd",                        // day
                    "---ddZ",
                    "---ddzzzzzz",
                    "--MM--",                       // month
                    "--MM--Z",
                    "--MM--zzzzzz",
                };
            }
        }

        [Obsolete("Use XmlConvert.ToDateTime() that accepts an XmlDateTimeSerializationMode instead.")]
        public static DateTime ToDateTime(string s)
        {
            return ToDateTime(s, AllDateTimeFormats);
        }

        public static DateTime ToDateTime(string s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format)
        {
            return DateTime.ParseExact(s, format, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite);
        }

        public static DateTime ToDateTime(string s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string[] formats)
        {
            return DateTime.ParseExact(s, formats, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite);
        }

        public static DateTime ToDateTime(string s, XmlDateTimeSerializationMode dateTimeOption)
        {
            XsdDateTime xsdDateTime = new XsdDateTime(s, XsdDateTimeFlags.AllXsd);
            DateTime dt = (DateTime)xsdDateTime;

            switch (dateTimeOption)
            {
                case XmlDateTimeSerializationMode.Local:
                    dt = SwitchToLocalTime(dt);
                    break;

                case XmlDateTimeSerializationMode.Utc:
                    dt = SwitchToUtcTime(dt);
                    break;

                case XmlDateTimeSerializationMode.Unspecified:
                    dt = new DateTime(dt.Ticks, DateTimeKind.Unspecified);
                    break;

                case XmlDateTimeSerializationMode.RoundtripKind:
                    break;

                default:
                    throw new ArgumentException(SR.Format(SR.Sch_InvalidDateTimeOption, dateTimeOption, nameof(dateTimeOption)));
            }
            return dt;
        }

        public static DateTimeOffset ToDateTimeOffset(string s)
        {
            ArgumentNullException.ThrowIfNull(s);

            XsdDateTime xsdDateTime = new XsdDateTime(s, XsdDateTimeFlags.AllXsd);
            DateTimeOffset dateTimeOffset = (DateTimeOffset)xsdDateTime;
            return dateTimeOffset;
        }

        public static DateTimeOffset ToDateTimeOffset(string s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string format)
        {
            ArgumentNullException.ThrowIfNull(s);

            return DateTimeOffset.ParseExact(s, format, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite);
        }

        public static DateTimeOffset ToDateTimeOffset(string s, [StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string[] formats)
        {
            ArgumentNullException.ThrowIfNull(s);

            return DateTimeOffset.ParseExact(s, formats, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AllowLeadingWhite | DateTimeStyles.AllowTrailingWhite);
        }

        public static Guid ToGuid(string s)
        {
            return new Guid(s);
        }

        internal static Exception? TryToGuid(string s, out Guid result)
        {
            Exception? exception = null;

            result = Guid.Empty;

            try
            {
                result = new Guid(s);
            }
            catch (ArgumentException)
            {
                exception = new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Guid"));
            }
            catch (FormatException)
            {
                exception = new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Guid"));
            }

            return exception;
        }

        private static DateTime SwitchToLocalTime(DateTime value) =>
            value.Kind switch
            {
                DateTimeKind.Local => value,
                DateTimeKind.Unspecified => new DateTime(value.Ticks, DateTimeKind.Local),
                DateTimeKind.Utc => value.ToLocalTime(),
                _ => value,
            };

        private static DateTime SwitchToUtcTime(DateTime value) =>
            value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Unspecified => new DateTime(value.Ticks, DateTimeKind.Utc),
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => value,
            };

        internal static Uri ToUri(string? s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                // string.Empty is a valid uri but not "   "
                s = TrimString(s);
                if (s.Length == 0 || s.IndexOf("##", StringComparison.Ordinal) != -1)
                {
                    throw new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Uri"));
                }
            }

            Uri? uri;
            if (!Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out uri))
            {
                throw new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Uri"));
            }

            return uri;
        }

        internal static Exception? TryToUri(string s, out Uri? result)
        {
            result = null;

            if (s != null && s.Length > 0)
            { //string.Empty is a valid uri but not "   "
                s = TrimString(s);
                if (s.Length == 0 || s.IndexOf("##", StringComparison.Ordinal) != -1)
                {
                    return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Uri"));
                }
            }
            if (!Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out result))
            {
                return new FormatException(SR.Format(SR.XmlConvert_BadFormat, s, "Uri"));
            }

            return null;
        }

        // Compares the given character interval and string and returns true if the characters are identical
        internal static bool StrEqual(char[]? chars, int strPos1, int strLen1, string str2)
        {
            if (strLen1 != str2.Length)
            {
                return false;
            }

            Debug.Assert(chars != null);

            int i = 0;
            while (i < strLen1 && chars[strPos1 + i] == str2[i])
            {
                i++;
            }

            return i == strLen1;
        }

        // XML whitespace characters, <spec>http://www.w3.org/TR/REC-xml#NT-S</spec>
        internal static readonly char[] WhitespaceChars = new char[] { ' ', '\t', '\n', '\r' };

        // Trim a string using XML whitespace characters
        internal static string TrimString(string value)
        {
            return value.Trim(WhitespaceChars);
        }

        // Trim beginning of a string using XML whitespace characters
        internal static string TrimStringStart(string value)
        {
            return value.TrimStart(WhitespaceChars);
        }

        // Trim end of a string using XML whitespace characters
        internal static string TrimStringEnd(string value)
        {
            return value.TrimEnd(WhitespaceChars);
        }

        // Split a string into a whitespace-separated list of tokens
        internal static string[] SplitString(string value)
        {
            return value.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries);
        }

        internal static string[] SplitString(string value, StringSplitOptions splitStringOptions)
        {
            return value.Split(WhitespaceChars, splitStringOptions);
        }

        internal static bool IsNegativeZero(double value)
        {
            // Simple equals function will report that -0 is equal to +0, so compare bits instead
            if (value == 0 && BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(-0e0))
            {
                return true;
            }
            return false;
        }

        internal static void VerifyCharData(string? data, ExceptionType exceptionType)
        {
            VerifyCharData(data, exceptionType, exceptionType);
        }

        internal static void VerifyCharData(string? data, ExceptionType invCharExceptionType, ExceptionType invSurrogateExceptionType)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            int i = 0;
            int len = data.Length;
            while (true)
            {
                while (i < len && XmlCharType.IsCharData(data[i]))
                {
                    i++;
                }
                if (i == len)
                {
                    return;
                }

                char ch = data[i];
                if (XmlCharType.IsHighSurrogate(ch))
                {
                    if (i + 1 == len)
                    {
                        throw CreateException(SR.Xml_InvalidSurrogateMissingLowChar, invSurrogateExceptionType, 0, i + 1);
                    }
                    ch = data[i + 1];
                    if (XmlCharType.IsLowSurrogate(ch))
                    {
                        i += 2;
                        continue;
                    }
                    else
                    {
                        throw CreateInvalidSurrogatePairException(data[i + 1], data[i], invSurrogateExceptionType, 0, i + 1);
                    }
                }
                throw CreateInvalidCharException(data, i, invCharExceptionType);
            }
        }

        internal static void VerifyCharData(char[] data, int offset, int len, ExceptionType exceptionType)
        {
            if (data == null || len == 0)
            {
                return;
            }

            int i = offset;
            int endPos = offset + len;
            while (true)
            {
                while (i < endPos && XmlCharType.IsCharData(data[i]))
                {
                    i++;
                }
                if (i == endPos)
                {
                    return;
                }

                char ch = data[i];
                if (XmlCharType.IsHighSurrogate(ch))
                {
                    if (i + 1 == endPos)
                    {
                        throw CreateException(SR.Xml_InvalidSurrogateMissingLowChar, exceptionType, 0, offset - i + 1);
                    }
                    ch = data[i + 1];
                    if (XmlCharType.IsLowSurrogate(ch))
                    {
                        i += 2;
                        continue;
                    }
                    else
                    {
                        throw CreateInvalidSurrogatePairException(data[i + 1], data[i], exceptionType, 0, offset - i + 1);
                    }
                }
                throw CreateInvalidCharException(data, len, i, exceptionType);
            }
        }

        internal static string EscapeValueForDebuggerDisplay(string value)
        {
            StringBuilder? sb = null;
            int i = 0;
            int start = 0;
            while (i < value.Length)
            {
                char ch = value[i];
                if ((int)ch < 0x20 || ch == '"')
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(value.Length + 4);
                    }
                    if (i - start > 0)
                    {
                        sb.Append(value, start, i - start);
                    }
                    start = i + 1;
                    switch (ch)
                    {
                        case '"':
                            sb.Append("\\\"");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        default:
                            sb.Append(ch);
                            break;
                    }
                }
                i++;
            }

            if (sb == null)
            {
                return value;
            }

            if (i - start > 0)
            {
                sb.Append(value, start, i - start);
            }

            return sb.ToString();
        }

        internal static Exception CreateException(string res, ExceptionType exceptionType, int lineNo, int linePos)
        {
            switch (exceptionType)
            {
                case ExceptionType.ArgumentException:
                    return new ArgumentException(res);
                case ExceptionType.XmlException:
                default:
                    return new XmlException(res, string.Empty, lineNo, linePos);
            }
        }

        internal static Exception CreateException(string res, string arg, ExceptionType exceptionType)
        {
            return CreateException(res, arg, exceptionType, 0, 0);
        }

        internal static Exception CreateException(string res, string arg, ExceptionType exceptionType, int lineNo, int linePos)
        {
            switch (exceptionType)
            {
                case ExceptionType.ArgumentException:
                    return new ArgumentException(string.Format(res, arg));
                case ExceptionType.XmlException:
                default:
                    return new XmlException(res, arg, lineNo, linePos);
            }
        }

        internal static Exception CreateException(string res, string[] args, ExceptionType exceptionType)
        {
            return CreateException(res, args, exceptionType, 0, 0);
        }

        internal static Exception CreateException(string res, string[] args, ExceptionType exceptionType, int lineNo, int linePos)
        {
            switch (exceptionType)
            {
                case ExceptionType.ArgumentException:
                    return new ArgumentException(string.Format(res, args));
                case ExceptionType.XmlException:
                default:
                    return new XmlException(res, args, lineNo, linePos);
            }
        }

        internal static Exception CreateInvalidSurrogatePairException(char low, char hi)
        {
            return CreateInvalidSurrogatePairException(low, hi, ExceptionType.ArgumentException);
        }

        internal static Exception CreateInvalidSurrogatePairException(char low, char hi, ExceptionType exceptionType)
        {
            return CreateInvalidSurrogatePairException(low, hi, exceptionType, 0, 0);
        }

        internal static Exception CreateInvalidSurrogatePairException(char low, char hi, ExceptionType exceptionType, int lineNo, int linePos)
        {
            string[] args = new string[] {
                ((uint)hi).ToString("X", CultureInfo.InvariantCulture),
                ((uint)low).ToString("X", CultureInfo.InvariantCulture)
            };
            return CreateException(SR.Xml_InvalidSurrogatePairWithArgs, args, exceptionType, lineNo, linePos);
        }

        internal static Exception CreateInvalidHighSurrogateCharException(char hi)
        {
            return CreateInvalidHighSurrogateCharException(hi, ExceptionType.ArgumentException);
        }

        internal static Exception CreateInvalidHighSurrogateCharException(char hi, ExceptionType exceptionType)
        {
            return CreateInvalidHighSurrogateCharException(hi, exceptionType, 0, 0);
        }

        internal static Exception CreateInvalidHighSurrogateCharException(char hi, ExceptionType exceptionType, int lineNo, int linePos)
        {
            return CreateException(SR.Xml_InvalidSurrogateHighChar, ((uint)hi).ToString("X", CultureInfo.InvariantCulture), exceptionType, lineNo, linePos);
        }

        internal static Exception CreateInvalidCharException(char[] data, int length, int invCharPos, ExceptionType exceptionType)
        {
            return CreateException(SR.Xml_InvalidCharacter, XmlException.BuildCharExceptionArgs(data, length, invCharPos), exceptionType, 0, invCharPos + 1);
        }

        internal static Exception CreateInvalidCharException(string data, int invCharPos)
        {
            return CreateInvalidCharException(data, invCharPos, ExceptionType.ArgumentException);
        }

        internal static Exception CreateInvalidCharException(string data, int invCharPos, ExceptionType exceptionType)
        {
            return CreateException(SR.Xml_InvalidCharacter, XmlException.BuildCharExceptionArgs(data, invCharPos), exceptionType, 0, invCharPos + 1);
        }

        internal static Exception CreateInvalidCharException(char invChar, char nextChar)
        {
            return CreateInvalidCharException(invChar, nextChar, ExceptionType.ArgumentException);
        }

        internal static Exception CreateInvalidCharException(char invChar, char nextChar, ExceptionType exceptionType)
        {
            return CreateException(SR.Xml_InvalidCharacter, XmlException.BuildCharExceptionArgs(invChar, nextChar), exceptionType);
        }

        internal static Exception CreateInvalidNameCharException(string name, int index, ExceptionType exceptionType)
        {
            return CreateException(index == 0 ? SR.Xml_BadStartNameChar : SR.Xml_BadNameChar, XmlException.BuildCharExceptionArgs(name, index), exceptionType, 0, index + 1);
        }
    }
}
