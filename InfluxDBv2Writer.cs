using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Vibrant.InfluxDB.Client;
using Vibrant.InfluxDB.Client.Rows;

namespace moritz_franz_outlook_com.Logic.InfluxDBWriter
{
    /// <summary>
    /// InfluxDBv2Writer writes double values to InfluxDB v2.0 using the "InfluxDB 2.0 compatibility API" <see cref="https://docs.influxdata.com/influxdb/v2.0/api/"/>.
    /// Parameters that must be specified in GPA are the following:
    /// - <see cref="IP"/> and <see cref="Port"/> of InfluxDB
    /// - <see cref="AuthorizationToken"/> of InfluxDB 2.0. (With write access for a "bucket".)
    /// - <see cref="Bucket"/> of InfluxDB to write to.
    /// 
    /// In GPA the "Config" text-fields have to be configured manually to succesfully send data to a bucket.
    /// Scheme for "Config" text-field:
    /// measurementName,tagKey1=tagValue,tagKey2=tagValue,...,tagKeyX=tagValue fieldKey1,fieldKey2,...,fieldKeyX
    /// 
    /// measurementName :   choose name of the value for "_measurement"
    /// tagKey=tagValue :   car=audi as an example, (tags can be used to catagorize your data)
    /// fieldKey        :   name of the "_field" in InfluxDB
    /// 
    /// For each "Input" you can write the same value to multiple _fields.
    /// You can assign multiple tags to the value of the field you are writing to.
    /// 
    /// Example:
    /// Tracking temperature in a house with bottom, middle and top floor and rooms which are labeled with numbers.
    /// When you want to write to InfluxDB, you'll have to choose/create a "bucket" to write to.
    /// e.g. bucket name = "temperatureBucket"
    /// Assigning the temperature values to Input 1 you'll have to configure "Config 1" as follows:
    /// temperatureBucket,floor=bottomFloor,room=roomNumber42 temperatureInFarad
    /// 
    /// With the above configuration of Input 1 you'll send temperature data of the bottom floor of room 42 to InfluxDB's bucket "temperatureBucket".
    /// The value is written to field-value "temperatureInFarad"
    /// 
    /// Credit to <see cref="https://github.com/alramlechner"/> for the HTTP-request related code <see cref="https://github.com/alramlechner/CommonLogicNodes/blob/master/InfluxDbNode/WriteNode.cs"/> I've used as a blueprint.
    /// </summary>
    public class InfluxDBv2Writer : LocalizablePrefixLogicNodeBase
    {
        private const string InputPrefix = "Input";
        private ITypeService typeService;

        /// <summary>
        /// URL used to write to InfluxDB. Getting initialized at "start-up" of the logic-node.
        /// </summary>
        private String URL;
        
        /// <summary>
        /// client used to write to InfluxDB. Getting initialized at "start-up" of the logic-node.
        /// </summary>
        private HttpWebRequest client;

        /// <summary>
        /// Constructor for logic-node "InfluxDBv2Writer".
        /// </summary>
        /// <param name="context"></param>
        public InfluxDBv2Writer(INodeContext context) : base(context, InputPrefix)
        {
            context.ThrowIfNull("context");
            this.typeService = context.GetService<ITypeService>();

            // Initializing Inputs- and Configs-list
            this.Inputs = new List<AnyValueObject>();
            this.Configs = new List<StringValueObject>();

            this.InputCount = this.typeService.CreateInt(PortTypes.Integer, "Amount of inputs", 1);
            this.InputCount.MinValue = 1;
            this.InputCount.MaxValue = 80;

            // Helper functions for growing/shrinking the list of "Inputs" and "Configs" whenever "InputCount" is changed
            ListHelpers.ConnectListToCounter(this.Inputs, this.InputCount, this.typeService.GetValueObjectCreator(PortTypes.Any, InputPrefix), null, null);
            ListHelpers.ConnectListToCounter(this.Configs, this.InputCount, this.typeService.GetValueObjectCreator(PortTypes.String, "Config of " + InputPrefix + " "), null, null);

            this.IP = this.typeService.CreateString(PortTypes.String, "IP of InfluxDB", "127.0.0.1");
            this.Port = this.typeService.CreateString(PortTypes.String, "Port of InfluxDB", "8086");
            this.AuthorizationToken = this.typeService.CreateString(PortTypes.String, "Authorization Token", "TokenWithWriteAccess");
            this.Bucket = this.typeService.CreateString(PortTypes.String, "Bucket name", "myInfluxdbBucketName");
            this.Organization = this.typeService.CreateString(PortTypes.String, "Organization", "myInfluxdbOrganizationName");

            // Debugging Output
            this.Output = typeService.CreateString(PortTypes.String, "Output", "Debugging");
            this.ErrorCode = typeService.CreateInt(PortTypes.Integer, "HTTP status-code");
            this.ErrorMessage = typeService.CreateString(PortTypes.String, "SMTP User");
        }

        /**
         * Inputs
         */
        // IP-address and port for connecting to influxdb
        [Parameter(DisplayOrder = 1, InitOrder = 1, IsDefaultShown = false, IsRequired = true)]
        public StringValueObject IP { get; private set; }
        [Parameter(DisplayOrder = 2, InitOrder = 2, IsDefaultShown = false, IsRequired = true)]
        public StringValueObject Port { get; private set; }

        // InfluxDb organization name
        [Parameter(DisplayOrder = 3, InitOrder = 3, IsDefaultShown = false, IsRequired = true)]
        public StringValueObject Organization { get; private set; }

        // InfluxDb token for authorization
        [Parameter(DisplayOrder = 4, InitOrder = 4, IsDefaultShown = false, IsRequired = true)]
        public StringValueObject AuthorizationToken { get; private set; }

        // InfluxDb bucket name
        [Parameter(DisplayOrder = 5, InitOrder = 5, IsDefaultShown = true, IsRequired = true)]
        public StringValueObject Bucket { get; private set; }

        // Number of Inputs
        [Parameter(DisplayOrder = 6, InitOrder = 6, IsDefaultShown = false, IsRequired = true)]
        public IntValueObject InputCount { get; private set; }

        // List of config text-fields
        [Parameter(DisplayOrder = 7, InitOrder = 7, IsDefaultShown = false, IsRequired = false)]
        public IList<StringValueObject> Configs { get; private set; }

        // List of input text-fields
        [Input(DisplayOrder = 8, InitOrder = 8, IsRequired = false)]
        public IList<AnyValueObject> Inputs { get; private set; }


        // Debugging Outputs
        [Output(DisplayOrder = 1, IsRequired = false, IsDefaultShown = false)]
        public StringValueObject Output { get; private set; }
        [Output(DisplayOrder = 2, IsRequired = false, IsDefaultShown = false)]
        public IntValueObject ErrorCode { get; private set; }
        [Output(DisplayOrder = 3, IsRequired = false, IsDefaultShown = false)]
        public StringValueObject ErrorMessage { get; private set; }




        /// <summary>
        /// This function is called every time any input (marked by attribute [Input]) receives a value and no input has no value.
        /// The inputs that were updated for this function to be called, have <see cref="IValueObject.WasSet"/> set to true. After this function returns 
        /// the <see cref="IValueObject.WasSet"/> flag will be reset to false.
        /// </summary>
        public override void Execute()
        {
            List<int> indexList = this.identifyWasSetIndex();
            if (indexList.Count != 0)
            {
                foreach (int index in indexList)
                {
                    WriteDatapointAsync(index);
                }
            }
        }

        /// <summary>
        /// Identify the indices of the Inputs that has triggered the logic-blocks execution.
        /// </summary>
        /// <returns> List of indices that belong to the ports which has triggered the logic-node. </returns>
        private List<int> identifyWasSetIndex()
        {
            List<int> indexList = new List<int>();
            for (int index = 0; index < this.InputCount.Value; index++)
            {
                AnyValueObject tmp = this.Inputs.ElementAt(index);
                if (tmp.WasSet)
                {
                    indexList.Add(index);
                }
            }
            return indexList;
        }

        /// <summary>
        /// Handles writing-operation for each Input that has triggered the logic-node.
        /// </summary>
        /// <param name="inputIndex">List of indices which have triggered the logic-node.</param>
        public void WriteDatapointAsync(int inputIndex)
        {
            var thread = new Thread(() => {
                WriteDatapointSync(
                    (errorCode, errorMessage) =>
                    {
                        if (errorCode != null)
                        {
                            ErrorCode.Value = errorCode.Value;
                        }
                        if (errorMessage != null)
                        {
                            ErrorMessage.Value = errorMessage;
                        }
                    }, inputIndex);
            });
            thread.Start();
        }

        /// <summary>
        /// Writes to InfluxDB v2 using the HTTP REST API.
        /// Credit to <see cref="https://github.com/alramlechner"/>.
        /// </summary>
        /// <param name="SetResultCallback"></param>
        /// <param name="inputIndex"></param>
        public void WriteDatapointSync(Action<int?, string> SetResultCallback,int inputIndex)
        {
            String Body = buildBodyString(inputIndex);
            try
            {   
                using (var request = client.GetRequestStream())
                {
                    using (var writer = new StreamWriter(request))
                    {
                        writer.Write(Body);
                    }
                }

                var response = client.GetResponse();
                using (var result = response.GetResponseStream())
                {
                    using (var reader = new StreamReader(result))
                    {
                        SetResultCallback(null, null);
                    }
                }
            }
            catch (WebException e)
            {
                if (e.Response is HttpWebResponse errorResponse)
                {
                    using (var result = errorResponse.GetResponseStream())
                    {
                        using (var reader = new StreamReader(result))
                        {
                            SetResultCallback((int)errorResponse.StatusCode, reader.ReadToEnd());
                            return;
                        }
                    }
                }
                SetResultCallback(998, "Unknown error");
            }
            catch (Exception e)
            {
                SetResultCallback(999, e.Message);
                return;
            }
        }

        /// <summary>
        /// Builds string suitable for InfluxDBv2 API using the information of config.
        /// "Configs" text-field in GPA has to be configured as followed:
        /// measurementName,tagKey1=tagValue,tagKey2=tagValue,...,tagKeyX=tagValue fieldKey1,fieldKey2,...,fieldKeyX
        /// 
        /// Gets converted to string as in the InfluxDB v2 documentation. <see cref="https://docs.influxdata.com/influxdb/v2.0/write-data/developer-tools/api/"/>
        /// </summary>
        /// <param name="inputIndex"></param>
        /// <returns>String that can be added to HTTP-request for write-operation for InfluxDB.</returns>
        private String buildBodyString(int inputIndex)
        {
            String[] splittedConfigStringArray = this.Configs.ElementAt(inputIndex).Value.Split(' ');
            String[] fieldKeyArray = splittedConfigStringArray.ElementAt(1).Split(',');
            String fieldKeyValuePairsArray = " ";
            foreach (var fieldKey in fieldKeyArray)
            {
                fieldKeyValuePairsArray += fieldKey + "=" + this.Inputs.ElementAt(inputIndex).Value + ",";
            }
            return splittedConfigStringArray[0] + fieldKeyValuePairsArray.Remove(fieldKeyValuePairsArray.Length - 1);
        }

        /// <summary>
        /// This function is called only once when the logic page is being loaded.
        /// Initializes HTTP-client for writing to InfluxDB.
        /// Initializes Inputs so that values are being written to InfluxDB, even if not all Input has been active.
        /// </summary>
        public override void Startup()
        {
            // configure Http-Request
            this.URL = "http://" + this.IP.Value + ":" + this.Port.Value + "/api/v2/write?org=" + this.Organization.Value + "&bucket=" + this.Bucket.Value + "&precision=ms";
            this.client = (HttpWebRequest)HttpWebRequest.Create(new Uri(URL));
            client.Headers.Add("Authorization", string.Format("Token {0}", this.AuthorizationToken.Value));
            client.Method = "POST";
            client.ContentType = "text/plain; charset=utf-8";

            // initialize every Input so that the incoming values can be written, even if not every Input received an value
            base.Startup();
            foreach(var gpaInput in this.Inputs)
            {
                gpaInput.Value = 0;
                gpaInput.WasSet = false;
            }
        }

        /// <summary>
        /// By default this function gets the translation for the node's in- and output from the <see cref="LogicNodeBase.ResourceManager"/>.
        /// A resource file with translation is required for this to work.
        /// </summary>
        /// <param name="language">The requested language, for example "en" or "de".</param>
        /// <param name="key">The key to translate.</param>
        /// <returns>The translation of <paramref name="key"/> in the requested language, or <paramref name="key"/> if the translation is missing.</returns>
        public override string Localize(string language, string key)
        {
            return base.Localize(language, key);
        }
    }
}
