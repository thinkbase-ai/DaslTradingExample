using CsvHelper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DaslTradingExample
{
    class Program
    {
        static void Main(string[] args)
        {
            ProcessSequence().Wait();
        }

        public static async Task ProcessSequence()
        {
            var initial_balance = "10000";
            //get the data and ruleset from the exe
            var ddata = new DaslData();
            var csv = new CsvReader(new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("DaslTradingExample.GBP_USD.csv")));
            var records = csv.GetRecords<TradingRecord>().ToList();
            ddata.code = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("DaslTradingExample.trading_simulation.darl")).ReadToEnd();
            //convert the csv records to a DaslSet.
            ddata.history = new DaslSet();
            ddata.history.sampleTime = new TimeSpan(1, 0, 0, 0); // 1 day
            ddata.history.events = new List<DaslState>();
            foreach (var r in records)
            {
                if (r == records.Last()) //records are in reverse order, set the initial value of the balance and add the first price
                    ddata.history.events.Add(new DaslState { timeStamp = DateTime.Parse(r.Date), values = new List<DarlVar> { new DarlVar { name = "price", Value = r.Price, dataType = DarlVar.DataType.numeric }, new DarlVar { name = "balance", dataType = DarlVar.DataType.numeric, Value = initial_balance } } });
                else // add the day's price
                    ddata.history.events.Add(new DaslState { timeStamp = DateTime.Parse(r.Date), values = new List<DarlVar> { new DarlVar { name = "price", Value = r.Price, dataType = DarlVar.DataType.numeric } } });
            }
            //send the data off to the simulator
            var valueString = JsonConvert.SerializeObject(ddata);
            var client = new HttpClient();
            var response = await client.PostAsync("https://darl.ai/api/Linter/DaslSimulate", new StringContent(valueString, Encoding.UTF8, "application/json"));
            var resp = await response.Content.ReadAsStringAsync();
            var returnedData =  JsonConvert.DeserializeObject<DaslSet>(resp);
            //now write out the results as a csv file 
            var outlist = new List<OutputRecord>();
            foreach(var d in returnedData.events)
            {
                var sterling = d.values.Where(a => a.name == "sterling").First();
                var ave3 = d.values.Where(a => a.name == "tradingrules.average3").First();
                var ave9 = d.values.Where(a => a.name == "tradingrules.average9").First();
                var price = d.values.Where(a => a.name == "price").First();
                var newbalance = d.values.Where(a => a.name == "newbalance").First();
                outlist.Add(new OutputRecord {
                    price = price.values[0],
                    date = d.timeStamp,
                    sterling = sterling.unknown ? 0.0 : sterling.values[0],
                    ave3 = ave3.unknown ? 0.0 : ave3.values[0],
                    ave9 = ave9.unknown ? 0.0 : ave9.values[0],
                    newbalance = newbalance.unknown ? 0.0 : newbalance.values[0],
                    trade = d.values.Where(a => a.name == "trade").First().Value,
                    transact = d.values.Where(a => a.name == "tradingsim.transact").First().Value
                });
            }
            var csvout = new CsvWriter(new StreamWriter("results.csv"));
            csvout.WriteRecords(outlist);
        }
    }

    public class DaslData
    {
        /// <summary>
        /// The code
        /// </summary>
        public string code { get; set; }

        /// <summary>
        /// The time series history
        /// </summary>
        public DaslSet history { get; set; }
    }

    public class DaslSet
    {
        /// <summary>
        /// Gets or sets the events.
        /// </summary>
        /// <value>
        /// The events.
        /// </value>
        [Required]
        [Display(Name = "The sequence of events", Description = "A sequence of time-tagged sets of values")]
        public List<DaslState> events { get; set; } = new List<DaslState>();

        /// <summary>
        /// Gets or sets the sample time.
        /// </summary>
        /// <value>
        /// The sample time.
        /// </value>
        [Required]
        [Display(Name = "The sample time", Description = "Will be used to set up the sample time of the simulation")]
        public TimeSpan sampleTime { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        /// <value>
        /// The description.
        /// </value>
        [Display(Name = "The description", Description = "Description of the contained sampled events")]
        public string description { get; set; }
    }

    /// <summary>
    /// Class DaslState.
    /// </summary>
    /// <remarks>A time stamped state of a system, reconstructible from the associated values</remarks>
    public class DaslState
    {
        /// <summary>
        /// Gets or sets the time stamp.
        /// </summary>
        /// <value>The time stamp.</value>
        [Required]
        [Display(Name = "The time stamp", Description = "The moment these values changed or became valid")]
        public DateTime timeStamp { get; set; }

        /// <summary>
        /// Gets or sets the values.
        /// </summary>
        /// <value>The values.</value>
        [Required]
        [Display(Name = "The values", Description = "A set of values that changed or became valid at the given time")]
        public List<DarlVar> values { get; set; }
    }

    /// <summary>
    /// Class DarlVar.
    /// </summary>
    /// <remarks>A general representation of a data value containing related uncertainty information from a fuzzy/possibilistic perspective.</remarks>
    [Serializable]
    public partial class DarlVar
    {
        /// <summary>
        /// The type of data stored in the DarlVar
        /// </summary>
        public enum DataType
        {
            /// <summary>
            /// Numeric including fuzzy
            /// </summary>
            numeric,
            /// <summary>
            /// One or more categories with confidences
            /// </summary>
            categorical,
            /// <summary>
            /// Textual
            /// </summary>
            textual,

        }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string name { get; set; }

        /// <summary>
        /// This result is unknown if true.
        /// </summary>
        /// <value><c>true</c> if unknown; otherwise, <c>false</c>.</value>
        public bool unknown { get; set; } = false;
        /// <summary>
        /// The confidence placed in this result
        /// </summary>
        /// <value>The weight.</value>
        public double weight { get; set; } = 1.0;

        /// <summary>
        /// The array containing the up to 4 values representing the fuzzy number.
        /// </summary>
        /// <value>The values.</value>
        /// <remarks>Since all fuzzy numbers used by DARL are convex, i,e. their envelope doesn't have any in-folding
        /// sections, the user can specify numbers with a simple sequence of doubles.
        /// So 1 double represents a crisp or singleton value.
        /// 2 doubles represent an interval,
        /// 3 a triangular fuzzy set,
        /// 4 a trapezoidal fuzzy set.
        /// The values must be ordered in ascending value, but it is permissible for two or more to hold the same value.</remarks>
        public List<double> values { get; set; }

        /// <summary>
        /// list of categories, each indexed against a truth value.
        /// </summary>
        /// <value>The categories.</value>
        public Dictionary<string, double> categories { get; set; }


        public List<DateTime> times { get; set; }

        /// <summary>
        /// Indicates approximation has taken place in calculating the values.
        /// </summary>
        /// <value><c>true</c> if approximate; otherwise, <c>false</c>.</value>
        /// <remarks>Under some circumstances the coordinates of the fuzzy number
        /// in "values" may not exactly represent the "cuts" values.</remarks>
        public bool approximate { get; set; }


        /// <summary>
        /// Gets or sets the type of the data.
        /// </summary>
        /// <value>The type of the data.</value>
        public DataType dataType { get; set; }

        /// <summary>
        /// Gets or sets the sequence.
        /// </summary>
        /// <value>The sequence.</value>
        public List<List<string>> sequence { get; set; }

        /// <summary>
        /// Single central or most confident value, expressed as a string.
        /// </summary>
        /// <value>The value.</value>
        public string Value { get; set; } = string.Empty;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"name = {name}, datatype = {dataType.ToString()} Central value: {Value}, isUnknown = {unknown.ToString()}, confidence = {weight} ");
            switch (dataType)
            {
                case DataType.numeric:
                    if (values.Count > 1)//fuzzy value
                    {
                        var vals = string.Join(',', values);
                        sb.Append($"Fuzzy numeric values = {vals}");
                    }
                    break;
                case DataType.categorical:
                    if (categories.Count > 1)//fuzzy value
                    {
                        sb.Append($"Fuzzy categorical values = ");
                        foreach (var c in categories.Keys)
                        {
                            sb.Append($"category: {c} confidence: {categories[c].ToString()}, ");
                        }
                    }
                    break;
            }
            return sb.ToString();
        }
    }

    public class OutputRecord
    {
        public DateTime date { get; set; }
        public double price { get; set; }
        public string trade { get; set; }
        public double sterling { get; set; }
        public double ave3 { get; set; }
        public double ave9 { get; set; }
        public string transact { get; set; }
        public double newbalance { get; set; }
    }
}
