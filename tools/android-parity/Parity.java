package com.example.yaesucatcontroladv;
public class Parity {
    static void ignore(Object... o) {}
    public static void main(String[] a) {
        String T = "CQ CQ CQ DE YO9VA YO9VA YO9VA PSE K\nTHE QUICK BROWN FOX JUMPS OVER THE LAZY DOG 1234567890\nUR RST 599 599 NAME EUGEN QTH PLOIESTI KN 35 73 SK\n";
        double[] snrs = {Double.POSITIVE_INFINITY, 10, 0, -3, -6, -9};
        for (double snr : snrs) for (int seed = 1; seed <= 2; seed++) for (int mf = 0; mf < 2; mf++) {
            String d = RttyDemodulatorTest.decodeaza(T, snr, mf == 1, seed);
            System.out.println("CASE snr=" + snr + " seed=" + seed + " mf=" + mf + " |" + d.replace("\n","\\n") + "|");
        }
        // ATC + fading, NB, DNR, SNRG
        short[] f = RttyDemodulatorTest.genereaza(T + T, 10.0, 1, 0, RttyDemodulator.BAUD_RTTY, 0.2);
        System.out.println("CASE atc |" + RttyDemodulatorTest.decodeaza(f, false, d -> d.folosesteAtc = true).replace("\n","\\n") + "|");
        short[] g = RttyDemodulatorTest.genereaza(T, -3.0, 2, 0, RttyDemodulator.BAUD_RTTY, 0);
        System.out.println("CASE nbdnr |" + RttyDemodulatorTest.decodeaza(g, false, d -> { d.folosesteNoiseBlanker = true; d.folosesteDnr = true; d.folosesteSnrGating = true; d.snrGatingK = 1.3; }).replace("\n","\\n") + "|");
        // AFC
        short[] h = RttyDemodulatorTest.genereaza(T + T, 20.0, 1, 12, RttyDemodulator.BAUD_RTTY, 0);
        RttyDemodulator dm = new RttyDemodulator(); dm.modUrmarireFrecventa = 2;
        dm.setListener(new RttyDemodulator.Listener(){ public void onCodBaudot(int c){} public void onStare(String s,int f){} });
        dm.porneste(48000);
        short[] buf = new short[3840];
        for (int p = 0; p < h.length; p += 3840) { int n = Math.min(3840, h.length - p); System.arraycopy(h, p, buf, 0, n); dm.proceseazaBloc(buf, n); }
        System.out.println("AFC mark=" + dm.frecventaTunedBaza);
    }
}
