using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assimilated.DMVRU.Util
{
    public class ConsoleSpinner
    {
        static readonly Dictionary<int, string[]> Sequences = new Dictionary<int, string[]>()
                                                        {
                                                            {0, new []{ "/", "-", "\\", "|" }},
                                                            {1, new []{ ".", "o", "0", "o" }},
                                                            {2, new []{ "+", "x" }},
                                                            {3, new []{ "V", "<", "^", ">" }},
                                                            {4, new []{ ".   ", "..  ", "... ", "...." }}
                                                        };
        int _counter;
        readonly string[] _sequence;
        
        private ConsoleSpinner(int spinnerType)
        {
            _sequence = Sequences.Count < spinnerType || spinnerType < 0 ? Sequences[0] : Sequences[spinnerType];
        }

        private Task Task { get; set; }
        private CancellationTokenSource CancellationTokenSource { get; set; }
        private void Turn()
        {
            _counter++;

            if (_counter >= _sequence.Length)
                _counter = 0;

            Console.Write(_sequence[_counter]);
            Console.SetCursorPosition(Console.CursorLeft - _sequence[_counter].Length, Console.CursorTop);
        }

        /// <summary>
        /// Creates a spinner that runs on a separate thread in a 
        /// consistent speed.
        /// </summary>
        /// <param name="spinnerType">Choose between five (0-4) spinner types.</param>
        /// <param name="spinSpeed">Choose the amount of time between each spin (in ms)</param>
        /// <returns></returns>
        public static ConsoleSpinner Create(int spinnerType = 0, int spinSpeed = 100)
        {
            var spinner = new ConsoleSpinner(spinnerType) {CancellationTokenSource = new CancellationTokenSource()};
            var ct = spinner.CancellationTokenSource.Token;
            spinner.Task = new Task(() =>
                                    {
                                        while (!ct.IsCancellationRequested)
                                        {
                                            spinner.Turn();
                                            Thread.Sleep(spinSpeed);
                                        }
                                    }, spinner.CancellationTokenSource.Token);
            return spinner;
        }

        public void Start()
        {
            // save CursorVisible if allowed
            try
            {
                Console.CursorVisible = false;
            }
            catch (SecurityException) { }
            
            Task.Start();
        }

        public void Stop()
        {
            // reset CursorVisible if allowed
            try
            {
                Console.CursorVisible = true;
            }
            catch (SecurityException) { }
            CancellationTokenSource.Cancel();
        }
    }    
}
