using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace BingoEngine
{
    public sealed class Cardface
    {
        public const string CardfaceVersion = "bingo75.cardface.v1";
        public const int LowestBall = 1;
        public const int HighestBall = 75;
        public const int BandCount = 5;
        public const int BandSize = 15;
        public const int NumberCount = 24;

        private static readonly int[] NumbersPerBand = { 5, 5, 4, 5, 5 };

        private readonly int[][] _columns;
        private readonly HashSet<int> _numbers;

        private Cardface(int[][] columns, HashSet<int> numbers)
        {
            _columns = columns;
            _numbers = numbers;
        }

        /// <summary>
        /// Las caras ya calculadas.
        ///
        /// La cara de un carton es una funcion pura de su numero: el mismo numero da la misma
        /// cara siempre, hoy y dentro de diez años, que es justamente lo que la hace fiable.
        /// Volver a calcularla no averigua nada nuevo -- solo cuesta un HMAC.
        ///
        /// Y se calculaba MUCHO. La pantalla del organizador pregunta, por cada carton y por
        /// cada figura, que numeros le faltan; con treinta cartones y dos figuras son sesenta
        /// caras por repintada, y repinta cada vez que cae una bolita. Con doscientos cartones
        /// son cuatrocientas.
        ///
        /// No se desaloja nada: una cara son veinticuatro numeros, y los cartones de una
        /// parroquia se cuentan por cientos.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, Cardface>
            Known = new System.Collections.Concurrent.ConcurrentDictionary<long, Cardface>();

        public static Cardface Of(CardNumber number)
        {
            if (number is null) throw new ArgumentNullException(nameof(number));

            Cardface already;
            if (Known.TryGetValue(number.Value, out already)) return already;

            Cardface made = Build(number);

            Known[number.Value] = made;

            return made;
        }

        private static Cardface Build(CardNumber number)
        {
            DeterministicStream stream = new DeterministicStream(CardfaceVersion, number.Value);
            int[][] columns = new int[BandCount][];
            HashSet<int> numbers = new HashSet<int>();

            for (int band = 0; band < BandCount; band++)
            {
                columns[band] = Draw(stream, band, NumbersPerBand[band]);
                foreach (int drawn in columns[band]) numbers.Add(drawn);
            }

            return new Cardface(columns, numbers);
        }

        public IReadOnlyList<int> Column(int band)
        {
            if (band < 0 || band >= BandCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(band), band, "A band must be 0.." + (BandCount - 1) + ".");
            }

            return _columns[band];
        }

        public bool Has(int number) => _numbers.Contains(number);

        internal int NumberAt(Cell cell)
        {
            if (cell is null) throw new ArgumentNullException(nameof(cell));

            if (cell.IsFreeCenter)
            {
                throw new InvalidOperationException("The free center holds no number.");
            }

            int row = cell.Band == Cell.FreeBand && cell.Row > Cell.FreeRow ? cell.Row - 1 : cell.Row;
            return _columns[cell.Band][row];
        }

        private static int[] Draw(DeterministicStream stream, int band, int howMany)
        {
            int lowest = (band * BandSize) + LowestBall;
            int[] pool = new int[BandSize];
            for (int i = 0; i < BandSize; i++) pool[i] = lowest + i;

            for (int i = 0; i < howMany; i++)
            {
                int j = i + stream.Index(BandSize - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            int[] drawn = new int[howMany];
            Array.Copy(pool, drawn, howMany);
            return drawn;
        }

        private sealed class DeterministicStream
        {
            private readonly byte[] _key;
            private readonly long _seed;
            private byte[] _block = Array.Empty<byte>();
            private long _counter;
            private int _position;

            internal DeterministicStream(string version, long seed)
            {
                _key = Encoding.UTF8.GetBytes(version);
                _seed = seed;
            }

            internal int Index(int upperBound)
            {
                uint limit = uint.MaxValue - (uint.MaxValue % (uint)upperBound);
                uint value;
                do
                {
                    value = NextUInt32();
                }
                while (value >= limit);

                return (int)(value % (uint)upperBound);
            }

            private uint NextUInt32()
            {
                if (_block.Length - _position < sizeof(uint)) Refill();

                uint value = (uint)((_block[_position] << 24)
                    | (_block[_position + 1] << 16)
                    | (_block[_position + 2] << 8)
                    | _block[_position + 3]);
                _position += sizeof(uint);
                return value;
            }

            private void Refill()
            {
                byte[] message = Encoding.UTF8.GetBytes(_seed + "|" + _counter);
                _counter++;
                _block = HMACSHA256.HashData(_key, message);
                _position = 0;
            }
        }
    }
}
