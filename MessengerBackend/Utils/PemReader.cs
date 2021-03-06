#nullable disable
#pragma warning disable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DerConverter;
using DerConverter.Asn;
using DerConverter.Asn.KnownTypes;
using PemUtils;

// Copyright 2020 huysentruitw
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace MessengerBackend.Utils
{
    public class PemReader : IDisposable
    {
        private static readonly int[] RsaIdentifier = { 1, 2, 840, 113549, 1, 1, 1 };
        private readonly bool _disposeStream;
        private readonly Encoding _encoding;
        private readonly Stream _stream;
        private readonly StringReader _stringReader;

        public PemReader(Stream stream, bool disposeStream = false, Encoding encoding = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _disposeStream = disposeStream;
            _encoding = encoding ?? Encoding.UTF8;
        }

        public PemReader(StringReader stringReader) => _stringReader = stringReader;

        public void Dispose()
        {
            if (_disposeStream)
            {
                _stream.Dispose();
            }
        }

        public RSAParameters ReadRsaKey()
        {
            var parts = ReadPemParts();
            var headerFormat = ExtractFormat(parts.Header, false);
            var footerFormat = ExtractFormat(parts.Footer, true);

            if (!headerFormat.Equals(footerFormat))
            {
                throw new InvalidOperationException($"Header/footer format mismatch: {headerFormat}/{footerFormat}");
            }

            var derData = Convert.FromBase64String(parts.Body);
            var der = DerConvert.Decode(derData);

            if (headerFormat.Equals(PemFormat.Public))
            {
                return ReadPublicKey(der);
            }

            if (headerFormat.Equals(PemFormat.Rsa))
            {
                return ReadPrivateKey(der);
            }

            throw new NotImplementedException($"The format {headerFormat} is not yet implemented");
        }

        private PemParts ReadPemParts()
        {
            if (_stringReader != null)
            {
                return ExtractPemParts(_stringReader.ReadToEnd());
            }

            using var reader = new StreamReader(_stream, _encoding, true, 4096, true);
            return ExtractPemParts(reader.ReadToEnd());
        }

        private static PemParts ExtractPemParts(string pem)
        {
            var match = Regex.Match(pem,
                @"^(?<header>\-+\s?BEGIN[^-]+\-+)\s*(?<body>[^-]+)\s*(?<footer>\-+\s?END[^-]+\-+)\s*$");
            if (!match.Success)
            {
                throw new InvalidOperationException("Data on the stream doesn't match the required PEM format");
            }

            return new PemParts
            {
                Header = match.Groups["header"].Value,
                Body = match.Groups["body"].Value.RemoveWhitespace(),
                Footer = match.Groups["footer"].Value
            };
        }

        private static PemFormat ExtractFormat(string headerOrFooter, bool isFooter)
        {
            var beginOrEnd = isFooter ? "END" : "BEGIN";
            var match = Regex.Match(headerOrFooter, $@"({beginOrEnd})\s+(?<format>[^-]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                throw new InvalidOperationException($"Unrecognized {beginOrEnd}: {headerOrFooter}");
            }

            return PemFormat.Parse(match.Groups["format"].Value.Trim());
        }

        private static RSAParameters ReadPublicKey(DerAsnType der)
        {
            if (der == null)
            {
                throw new ArgumentNullException(nameof(der));
            }

            var outerSequence = der as DerAsnSequence;
            if (outerSequence == null)
            {
                throw new ArgumentException($"{nameof(der)} is not a sequence");
            }

            if (outerSequence.Value.Length != 2)
            {
                throw new InvalidOperationException("Outer sequence must contain 2 parts");
            }

            var headerSequence = outerSequence.Value[0] as DerAsnSequence;
            if (headerSequence == null)
            {
                throw new InvalidOperationException(
                    "First part of outer sequence must be another sequence (the header sequence)");
            }

            if (headerSequence.Value.Length != 2)
            {
                throw new InvalidOperationException("The header sequence must contain 2 parts");
            }

            var objectIdentifier = headerSequence.Value[0] as DerAsnObjectIdentifier;
            if (objectIdentifier == null)
            {
                throw new InvalidOperationException("First part of header sequence must be an object-identifier");
            }

            if (!objectIdentifier.Value.SequenceEqual(RsaIdentifier))
            {
                throw new InvalidOperationException(
                    $"RSA object-identifier expected 1.2.840.113549.1.1.1, got: {string.Join(".", objectIdentifier.Value.Select(x => x.ToString()))}");
            }

            if (!(headerSequence.Value[1] is DerAsnNull))
            {
                throw new InvalidOperationException("Second part of header sequence must be a null");
            }

            var innerSequenceBitString = outerSequence.Value[1] as DerAsnBitString;
            if (innerSequenceBitString == null)
            {
                throw new InvalidOperationException("Second part of outer sequence must be a bit-string");
            }

            var innerSequenceData = innerSequenceBitString.ToByteArray();
            var innerSequence = DerConvert.Decode(innerSequenceData) as DerAsnSequence;
            if (innerSequence == null)
            {
                throw new InvalidOperationException("Could not decode the bit-string as a sequence");
            }

            if (innerSequence.Value.Length < 2)
            {
                throw new InvalidOperationException(
                    "Inner sequence must at least contain 2 parts (modulus and exponent)");
            }

            return new RSAParameters
            {
                Modulus = GetIntegerData(innerSequence.Value[0]),
                Exponent = GetIntegerData(innerSequence.Value[1])
            };
        }

        private static RSAParameters ReadPrivateKey(DerAsnType der)
        {
            if (der == null)
            {
                throw new ArgumentNullException(nameof(der));
            }

            if (!(der is DerAsnSequence sequence))
            {
                throw new ArgumentException($"{nameof(der)} is not a sequence");
            }

            if (sequence.Value.Length != 9)
            {
                throw new InvalidOperationException("Sequence must contain 9 parts");
            }

            return new RSAParameters
            {
                Modulus = GetIntegerData(sequence.Value[1]),
                Exponent = GetIntegerData(sequence.Value[2]),
                D = GetIntegerData(sequence.Value[3]),
                P = GetIntegerData(sequence.Value[4]),
                Q = GetIntegerData(sequence.Value[5]),
                DP = GetIntegerData(sequence.Value[6]),
                DQ = GetIntegerData(sequence.Value[7]),
                InverseQ = GetIntegerData(sequence.Value[8])
            };
        }

        private static byte[] GetIntegerData(DerAsnType der)
        {
            var data = (der as DerAsnInteger)?.Encode(null);
            if (data == null)
            {
                throw new InvalidOperationException("Part does not contain integer data");
            }

            if (data[0] == 0x00)
            {
                data = data.Skip(1).ToArray();
            }

            return data;
        }

        private class PemParts
        {
            public string Header { get; set; }
            public string Body { get; set; }
            public string Footer { get; set; }
        }
    }
}