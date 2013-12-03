using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YAMP;

namespace RiemannApprox
{
    class ReimannSumModel : ReactiveObject
    {
        string _function;
        public string Function
        {
            get { return _function; }
            set { this.RaiseAndSetIfChanged(ref _function, value); }
        }

        int? _numRectangles;
        public int? numRectangles
        {
            get { return _numRectangles; }
            set
            {
                //Set a rectangle limit to keep from getting the UI locked up. TODO: Make this able to be disabled.
                //value = Math.Min(value.Value, 100000);
                this.RaiseAndSetIfChanged(ref _numRectangles, value);
            }
        }
        string _startVal;
        public string StartingVal
        {
            get { return _startVal; }
            set { this.RaiseAndSetIfChanged(ref _startVal, value); }
        }

        string _endVal;
        public string EndVal
        {
            get { return _endVal; }
            set { this.RaiseAndSetIfChanged(ref _endVal, value); }
        }
        double? _result;
        public double? LeftSum
        {
            get { return _result; }
            set { this.RaiseAndSetIfChanged(ref _result, value); }
        }
        double? _rightSum;
        public double? RightSum
        {
            get { return _rightSum; }
            set { this.RaiseAndSetIfChanged(ref _rightSum, value); }
        }
        double? _trapzoidalSum;
        public double? TrapezoidalSum
        {
            get { return _trapzoidalSum; }
            set { this.RaiseAndSetIfChanged(ref _trapzoidalSum, value); }
        }
        double? _midpointSum;
        public double? MidpointSum
        {
            get { return _midpointSum; }
            set { this.RaiseAndSetIfChanged(ref _midpointSum, value); }
        }

        private bool _stopOne = true;
        private object isRunning = new object();
        private void cancelOperations()
        {
        }
        public ReimannSumModel()
        {
            var canRun = this.WhenAny(x => x.Function,
                x => x.numRectangles,
                x => x.StartingVal,
                x => x.EndVal,
                (fx, rects, start, end) =>
                {
                    return CheckRun(fx.Value, rects.Value, start.Value, end.Value);
                });
            var runForVal = this.WhenAny(
                x => x.Function,
                x => x.numRectangles,
                x => x.StartingVal,
                x => x.EndVal,
                (fx, _rects, _start, _end) =>
                {
                    return new Lazy<Tuple<double, double, double, double>>(() =>
                    {
                        int rects = _rects.Value.Value;// Int32.Parse(_rects.Value);
                        double start = ParseEx(_start.Value);
                        double end = ParseEx(_end.Value);
                        double deltaX = (end - start) / rects;
                        string func = cleanFunc(fx.Value);
                        inc();
                        return Tuple.Create(
                           getRiemannSum(rects, start, end, deltaX, func, SumType.Left),
                           getRiemannSum(rects, start, end, deltaX, func, SumType.Right),
                           getRiemannSum(rects, start, end, deltaX, func, SumType.Middle),
                           getRiemannSum(rects, start, end, deltaX, func, SumType.Trapezoidal));

                    });
                });

            TimeSpan halfSecond = new TimeSpan(0, 0, 0, 0, 500);
            var validValues = Observable.Zip(canRun, runForVal, (x, y) => new { CanRun = x, Run = y })
                .Where(x => x.CanRun)
                .Throttle(halfSecond)
                //Run the calculations in the Task pool(i.e. background)
                .ObserveOn(TaskPoolScheduler.Default)
                .Select(x => x.Run.Value)
                //Bring the results back to the UI thread
                .SubscribeOnDispatcher();
            validValues.Subscribe(x =>
            {
                LeftSum = x.Item1;
                RightSum = x.Item2;
                MidpointSum = x.Item3;
                TrapezoidalSum = x.Item4;
            });
        }

        private bool CheckRun(string fx, int? rects,  string start, string end)
        {
            //Limit the scope of the parseHolders
            {
                double parseHolder;
                bool numsReady = rects.HasValue
                    && TryParseEx(start, out parseHolder)
                    && TryParseEx(end, out parseHolder)
                    && !String.IsNullOrWhiteSpace(fx);

                if (!numsReady)
                {
                    return false;
                }
            }
            //Don't try to parse if we don't need to. Reduce execption handling
            try
            {
                ScalarValue x = new ScalarValue(10);
                //Replace any x's that are not part of max or exp fucntion names. 
                string fxl = Regex.Replace(fx, @"(?<!ma|e)x(?!p)", "q", RegexOptions.IgnoreCase);
                //q is used to make sure that the multithreading of listening vs evaluation doesn't mess up the parser. The parser has a global context.
                Parser.AddVariable("q", x);
                string func = cleanFunc(fxl);
                Console.WriteLine(func);
                var junk = (ScalarValue)Parser.Parse(func).Execute();
            }
            catch (YAMPException ex)
            {
                return false;
            }
            catch (InvalidCastException ex)
            {
                return false;
            }

            catch
            {
                return false;
            }
            finally
            {
                Parser.RemoveVariable("q");
            }
            return true;
        }
        private static object _countLock = new object();
        private static int locked = 0;
        private static void inc()
        {
            lock (_countLock)
            {
                locked++;
                Console.WriteLine(locked);
            }
        }
        private double ParseEx(string input) 
        {
            double result;
            if (TryParseEx(input, out result))
            {
                return result;
            }
            else 
            {
                throw new ArgumentOutOfRangeException(String.Format("The expression \"{0}\" could not be parsed", input));
            }
        }
        private bool TryParseEx(string input, out double result)
        {
            if (String.IsNullOrWhiteSpace(input)) 
            {
                result = double.NaN;
                return false;
            }
            if (Double.TryParse(input, out result))
            {
                return true;
            }
            if (input == "e")
            {
                result = Math.E;
                return true;
            }
            if (input.ToLower() == "pi") 
            {
                result = Math.PI;
                return true;
            }
            //Guard against the Parser trying use x or q as a variable here. 
            if (input.Contains("x") || input.Contains("q"))
            {
                result = double.NaN;
                return false;
            }
            try
            {
                input = cleanFunc(input);
                var res = (ScalarValue)Parser.Parse(input).Execute();
                result = res.Value;
                return true;
            }
            catch 
            {
                result = double.NaN;
                return false;
            }

        }
        private string cleanFunc(string func)
        {
            //YAMP has a few deficencies as a permissive parser that need to be wrapped around to allow for common shortcuts. 
            //Note that if you add functions to the parser, you'll need to make sure that they get properly cleaned (or not) here.

            //Match any x that is not part of the calls to 'max' or 'exp' and surround it with parens
            func = Regex.Replace(func, @"(?<!ma|e)(x)(?!p)", "($1)", RegexOptions.IgnoreCase);
            //Match any q that is not part of the calls to 'sqrt'
            func = Regex.Replace(func, @"(?<!s)(q)(?!rt)", "($1)", RegexOptions.IgnoreCase); 
            //Take any parens and make sure that they get multiplied by numbers and parens next to them
            func = Regex.Replace(func, @"(\d)\(", "$1*(");
            func = Regex.Replace(func, @"\)(\d)", ")*$1");
            //Take two paren groups and make sure that they get multiplied properly
            func = Regex.Replace(func, @"\)\s*\(", ")*(");
            //Take any function or constant that has parens in front of it (but not behind) and multiply it.
            func = Regex.Replace(func, @"\)(\p{L}+)", ")*$1");
            //take any function and have it be multiplied by numbers next to it. 
            func = Regex.Replace(func, @"(\d)(\p{L}+)", "$1*$2");
            func = Regex.Replace(func, @"(\p{L}+)(\d)", "$2*$1");
            return func;
        }
        
        private static double getRiemannSum(int rects, double start, double end, double deltaX, string func, SumType sumSide)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            ScalarValue x = new ScalarValue(0);
            Parser.AddVariable("x", x);
            var c = Parser.Parse(func);
            double sum = 0.0;
            double middleOffset = 0;
            int first = 0;
            int limit = rects;
            double trapezoidalMultiple = 1.0;
            //sumSide == SumType.Left is the default case, in which none of these if statements will fire.
            if (sumSide == SumType.Middle)
            {
                middleOffset = deltaX / 2;
            }
            if (sumSide == SumType.Right)
            {
                first++;
                limit++;
            }
            if (sumSide == SumType.Trapezoidal)
            {
                trapezoidalMultiple = 2.0;
                //Shrink the sum calculated by removing a term on the right side/
                first++;
            }
            try
            {
                for (int i = first; i < limit; i++)
                {
                    x.Value = trapezoidalMultiple * (start + (i * deltaX) + middleOffset);
                    ScalarValue s = (ScalarValue)c.Execute();
                    sum += s.Value;
                }
                if (sumSide == SumType.Trapezoidal)
                {
                    x.Value = start;
                    ScalarValue s = (ScalarValue)c.Execute();
                    sum += s.Value;
                    x.Value = end;
                    s = (ScalarValue)c.Execute();
                    sum += s.Value;
                    sum *= 0.5;
                }
            }
            catch (YAMP.YAMPException ex)
            {
                Console.WriteLine(ex.Message);
                throw ex;
            }
            finally
            {
                Parser.RemoveVariable("x");
            }
            Console.WriteLine(watch.Elapsed);
            //Console.WriteLine(sum * deltaX);
            return sum * deltaX;
        }
        enum SumType
        {
            Left,
            Right,
            Middle,
            Trapezoidal,
        }
    }
}
