using System.Globalization;
using System.Text;
using HamContest;
using HamContest.Udc;

namespace RttyDecoder.Wpf.Contest;

/// <summary>ADI 3.1.8 export. ADI strings are printable ASCII; the UTF-8 session retains all original data.</summary>
public static class ContestAdif
{
    public static string Export(ContestSession session)
    {
        var state = session.CaptureState();
        var output = new StringBuilder("RTTY Decoder contest log\r\n");
        void Field(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (value.Any(c => c < 32 || c > 126)) throw new ArgumentException($"Campul ADIF {key} trebuie sa contina text ASCII imprimabil. Sesiunea originala ramane salvata integral.");
            output.Append('<').Append(key).Append(':').Append(value.Length).Append('>').Append(value);
        }
        Field("ADIF_VER", "3.1.8"); Field("PROGRAMID", "RTTYDecoder"); Field("PROGRAMVERSION", "0.1"); output.Append("<EOH>\r\n");
        foreach (var qso in state.Contacts.OrderBy(q => q.Time))
        {
            Field("CALL", qso.Station.Callsign); Field("STATION_CALLSIGN", state.OwnStation.Callsign);
            Field("QSO_DATE", qso.Time.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            Field("TIME_ON", qso.Time.UtcDateTime.ToString("HHmmss", CultureInfo.InvariantCulture));
            Field("BAND", qso.Band); Field("FREQ", (qso.FrequencyHz / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture));
            Field("MODE", "RTTY"); Field("RST_SENT", qso.Sent.Report); Field("RST_RCVD", qso.Received.Report);
            Field("STX", qso.Sent.Serial?.ToString(CultureInfo.InvariantCulture)); Field("SRX", qso.Received.Serial?.ToString(CultureInfo.InvariantCulture));
            Field("STX_STRING", string.Join(" ", new[] { qso.Sent.Serial?.ToString("D3", CultureInfo.InvariantCulture), qso.Sent.Value }.Where(v => !string.IsNullOrEmpty(v))));
            Field("SRX_STRING", string.Join(" ", new[] { qso.Received.Serial?.ToString("D3", CultureInfo.InvariantCulture), qso.Received.Value, qso.Received.Name, qso.Received.Misc, qso.Received.Section, qso.Received.Grid }.Where(v => !string.IsNullOrEmpty(v))));
            Field("NAME", qso.Received.Name); Field("GRIDSQUARE", qso.Received.Grid); Field("COMMENT", qso.Received.Comment);
            Field("CONTEST_ID", (session.Rules as UdcContestRules)?.CabrilloName ?? session.Rules.Name);
            output.Append("<EOR>\r\n");
        }
        return output.ToString();
    }
}
