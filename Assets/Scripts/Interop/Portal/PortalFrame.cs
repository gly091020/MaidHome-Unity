using System;
using System.IO;
using System.Text;
using MaidHome.Core.Json;

namespace MaidHome.Interop.Portal
{
    /// <summary>
    /// 一帧 = [4 字节大端 header 长度][header JSON(UTF-8)][body 原始字节]。
    /// body 长度写在 header 的 "body" 字段里，没有该字段就是 0。
    /// 拆成两段是为了让 Java 侧不用把二进制塞进 JSON。
    /// </summary>
    public static class PortalFrame
    {
        public const int MaxHeaderBytes = 256 * 1024;
        public const int MaxBodyBytes = 256 * 1024 * 1024;

        static readonly byte[] EmptyBytes = new byte[0];

        public static byte[] Encode(string headerJson, byte[] body)
        {
            byte[] header = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(headerJson) ? "{}" : headerJson);
            byte[] payload = body != null ? body : EmptyBytes;
            byte[] frame = new byte[4 + header.Length + payload.Length];
            WriteLength(frame, 0, header.Length);
            Buffer.BlockCopy(header, 0, frame, 4, header.Length);
            if (payload.Length > 0)
            {
                Buffer.BlockCopy(payload, 0, frame, 4 + header.Length, payload.Length);
            }

            return frame;
        }

        /// <summary>
        /// 读到一帧返回 true；对端正常关闭返回 false；半帧、超长 header、坏 JSON 直接抛异常。
        /// </summary>
        public static bool TryRead(Stream stream, out JsonValue header, out byte[] body)
        {
            header = null;
            body = null;

            byte[] lengthBuffer = new byte[4];
            int got = ReadFully(stream, lengthBuffer, 4, false);
            if (got == 0)
            {
                return false;
            }

            if (got < 4)
            {
                throw new EndOfStreamException("帧头只读到 " + got + " 字节");
            }

            int headerLength = ReadLength(lengthBuffer, 0);
            if (headerLength <= 0 || headerLength > MaxHeaderBytes)
            {
                throw new InvalidDataException("header 长度不合法: " + headerLength);
            }

            byte[] headerBytes = new byte[headerLength];
            ReadFully(stream, headerBytes, headerLength, true);
            header = MiniJson.Parse(Encoding.UTF8.GetString(headerBytes));

            int bodyLength = header["body"].AsInt(0);
            if (bodyLength < 0 || bodyLength > MaxBodyBytes)
            {
                throw new InvalidDataException("body 长度不合法: " + bodyLength);
            }

            if (bodyLength > 0)
            {
                body = new byte[bodyLength];
                ReadFully(stream, body, bodyLength, true);
            }

            return true;
        }

        /// <summary>JSON 字符串字面量，手写序列化时用。</summary>
        public static string Quote(string text)
        {
            StringBuilder result = new StringBuilder("\"");
            if (text != null)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (c)
                    {
                        case '"': result.Append("\\\""); break;
                        case '\\': result.Append("\\\\"); break;
                        case '\n': result.Append("\\n"); break;
                        case '\r': result.Append("\\r"); break;
                        case '\t': result.Append("\\t"); break;
                        default:
                            if (c < ' ')
                            {
                                result.Append("\\u").Append(((int)c).ToString("x4"));
                            }
                            else
                            {
                                result.Append(c);
                            }

                            break;
                    }
                }
            }

            return result.Append('"').ToString();
        }

        static int ReadFully(Stream stream, byte[] buffer, int count, bool exact)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            if (exact && total < count)
            {
                throw new EndOfStreamException("期望 " + count + " 字节，只读到 " + total);
            }

            return total;
        }

        static void WriteLength(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        static int ReadLength(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
        }
    }
}
