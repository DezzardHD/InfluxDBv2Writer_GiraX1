# InfluxDBv2Writer_GiraX1
Logic node for writing time series data to [InfluxDB v2.0](https://docs.influxdata.com/influxdb/v2.0/) via [HTTP REST API v2.0](https://docs.influxdata.com/influxdb/v2.0/reference/api/). This logic node can be used with [Gira X1](https://www.gira.de/produkte/lichtsteuerung/lichtsteuerung-per-app/gira-x1#) (Hardware).

## Supported Data Types
Currently data of type *Number* or *Binary* is supported.

# How to configure? (Gira Project Assistant)
1. Fill in IP and Port of your InfluxDB Server.
2. Use the organization name you had configured on your InfluxDB Server.
3. Use a **Authetification Token** with *write access* to your *bucket*. (You can find/create tokens in the graphical UI of InfluxDB or using the [CLI](https://docs.influxdata.com/influxdb/v2.0/security/tokens/))
4. Next to fill in is the name of the **bucket** you want to write the data to.
5. Now specify the number of *Inputs* you want to use.
6. Last but not least you'll have to configure each Input as followed:<br/>
`measurementName,tagKey1=tagValue,tagKey2=tagValue,...,tagKeyX=tagValue fieldKey1,fieldKey2,...,fieldKeyX`
