using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pal98.Challenges
{
    internal static class StrictJsonSyntaxValidator
    {
        public static void Validate(string json, int maximumDepth, int maximumNodes)
        {
            var parser = new Parser(json ?? string.Empty, maximumDepth, maximumNodes);
            parser.ParseDocument();
        }

        private sealed class Parser
        {
            private readonly string text;
            private readonly int maximumDepth;
            private readonly int maximumNodes;
            private int position;
            private int nodeCount;

            public Parser(string text, int maximumDepth, int maximumNodes)
            {
                this.text = text;
                this.maximumDepth = maximumDepth;
                this.maximumNodes = maximumNodes;
            }

            public void ParseDocument()
            {
                SkipWhitespace();
                ParseValue(0);
                SkipWhitespace();
                if (position != text.Length)
                {
                    Fail("根节点后存在多余内容");
                }
            }

            private void ParseValue(int depth)
            {
                if (depth > maximumDepth)
                {
                    throw new InvalidDataException(
                        "内容包 JSON 超过 PALDLL 的 " + maximumDepth + " 层嵌套限制。" );
                }
                nodeCount++;
                if (nodeCount > maximumNodes)
                {
                    throw new InvalidDataException(
                        "内容包 JSON 超过 PALDLL 的 " + maximumNodes + " 节点限制。" );
                }

                SkipWhitespace();
                if (position >= text.Length)
                {
                    Fail("值意外结束");
                }

                char current = text[position];
                if (current == '{')
                {
                    ParseObject(depth);
                }
                else if (current == '[')
                {
                    ParseArray(depth);
                }
                else if (current == '"')
                {
                    ParseString();
                }
                else if (current == '-' || current >= '0' && current <= '9')
                {
                    ParseNumber();
                }
                else if (MatchLiteral("true") || MatchLiteral("false") || MatchLiteral("null"))
                {
                    return;
                }
                else
                {
                    Fail("遇到无效值");
                }
            }

            private void ParseObject(int depth)
            {
                position++;
                SkipWhitespace();
                if (Consume('}'))
                {
                    return;
                }

                var keys = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    SkipWhitespace();
                    if (position >= text.Length || text[position] != '"')
                    {
                        Fail("对象键必须是字符串");
                    }
                    string key = ParseString();
                    if (!keys.Add(key))
                    {
                        Fail("对象包含重复键：" + key);
                    }

                    SkipWhitespace();
                    if (!Consume(':'))
                    {
                        Fail("对象键后缺少冒号");
                    }
                    ParseValue(depth + 1);

                    SkipWhitespace();
                    if (Consume('}'))
                    {
                        return;
                    }
                    if (!Consume(','))
                    {
                        Fail("对象条目后缺少逗号或右花括号");
                    }
                }
            }

            private void ParseArray(int depth)
            {
                position++;
                SkipWhitespace();
                if (Consume(']'))
                {
                    return;
                }

                while (true)
                {
                    ParseValue(depth + 1);
                    SkipWhitespace();
                    if (Consume(']'))
                    {
                        return;
                    }
                    if (!Consume(','))
                    {
                        Fail("数组条目后缺少逗号或右方括号");
                    }
                }
            }

            private string ParseString()
            {
                if (!Consume('"'))
                {
                    Fail("缺少字符串起始引号");
                }

                var value = new StringBuilder();
                while (position < text.Length)
                {
                    char current = text[position++];
                    if (current == '"')
                    {
                        return value.ToString();
                    }
                    if (current < 0x20)
                    {
                        Fail("字符串包含未转义控制字符");
                    }
                    if (current != '\\')
                    {
                        value.Append(current);
                        continue;
                    }

                    if (position >= text.Length)
                    {
                        Fail("字符串转义意外结束");
                    }
                    char escape = text[position++];
                    switch (escape)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u':
                            char high = ParseHex4();
                            if (char.IsHighSurrogate(high))
                            {
                                if (position + 2 > text.Length ||
                                    text[position] != '\\' || text[position + 1] != 'u')
                                {
                                    Fail("Unicode 高代理项后缺少低代理项");
                                }
                                position += 2;
                                char low = ParseHex4();
                                if (!char.IsLowSurrogate(low))
                                {
                                    Fail("Unicode 代理项配对无效");
                                }
                                value.Append(high);
                                value.Append(low);
                            }
                            else if (char.IsLowSurrogate(high))
                            {
                                Fail("Unicode 低代理项没有对应的高代理项");
                            }
                            else
                            {
                                value.Append(high);
                            }
                            break;
                        default:
                            Fail("字符串包含无效转义");
                            break;
                    }
                }

                Fail("字符串缺少结束引号");
                return null;
            }

            private char ParseHex4()
            {
                if (position + 4 > text.Length)
                {
                    Fail("Unicode 转义不足四位");
                }
                int value = 0;
                for (int index = 0; index < 4; index++)
                {
                    char digit = text[position++];
                    value <<= 4;
                    if (digit >= '0' && digit <= '9')
                    {
                        value += digit - '0';
                    }
                    else if (digit >= 'a' && digit <= 'f')
                    {
                        value += digit - 'a' + 10;
                    }
                    else if (digit >= 'A' && digit <= 'F')
                    {
                        value += digit - 'A' + 10;
                    }
                    else
                    {
                        Fail("Unicode 转义包含非十六进制字符");
                    }
                }
                return (char)value;
            }

            private void ParseNumber()
            {
                int start = position;
                Consume('-');
                if (position >= text.Length)
                {
                    Fail("数字意外结束");
                }
                if (text[position] == '0')
                {
                    position++;
                }
                else if (text[position] >= '1' && text[position] <= '9')
                {
                    ConsumeDigits();
                }
                else
                {
                    Fail("数字整数部分无效");
                }

                if (Consume('.'))
                {
                    if (position >= text.Length || text[position] < '0' || text[position] > '9')
                    {
                        Fail("数字小数部分无效");
                    }
                    ConsumeDigits();
                }
                if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
                {
                    position++;
                    if (position < text.Length && (text[position] == '+' || text[position] == '-'))
                    {
                        position++;
                    }
                    if (position >= text.Length || text[position] < '0' || text[position] > '9')
                    {
                        Fail("数字指数部分无效");
                    }
                    ConsumeDigits();
                }

                string numberText = text.Substring(start, position - start);
                double number;
                if (!double.TryParse(
                        numberText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out number) ||
                    double.IsNaN(number) || double.IsInfinity(number))
                {
                    Fail("数字超出 PALDLL 可接受范围");
                }
            }

            private void ConsumeDigits()
            {
                while (position < text.Length && text[position] >= '0' && text[position] <= '9')
                {
                    position++;
                }
            }

            private bool MatchLiteral(string literal)
            {
                if (position + literal.Length > text.Length ||
                    string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
                {
                    return false;
                }
                position += literal.Length;
                return true;
            }

            private bool Consume(char expected)
            {
                if (position >= text.Length || text[position] != expected)
                {
                    return false;
                }
                position++;
                return true;
            }

            private void SkipWhitespace()
            {
                while (position < text.Length)
                {
                    char current = text[position];
                    if (current != ' ' && current != '\t' && current != '\r' &&
                        current != '\n' && current != '\v' && current != '\f')
                    {
                        return;
                    }
                    position++;
                }
            }

            private void Fail(string message)
            {
                throw new InvalidDataException(
                    "严格 JSON 语法无效（字符位置 " + position + "）：" + message + "。" );
            }
        }
    }
}
