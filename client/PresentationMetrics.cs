using System;
using UnityEngine;

namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private readonly int[] _frameSamples = new int[10];
        private readonly double[] _frameTotals = new double[10];
        private readonly float[] _framePeaks = new float[10];
        private void SamplePresentation()
        {
            if (!_presentationDiagnostics.Value || !_open || !Application.isFocused || !(_laptopSession?.Ready ?? true) || Time.unscaledTime - _pageAt < .5f)
                return;
            var ms = Time.unscaledDeltaTime * 1000;
            _frameSamples[_page]++;
            _frameTotals[_page] += ms;
            _framePeaks[_page] = Mathf.Max(_framePeaks[_page], ms);
        }
        private void ReportPresentation()
        {
            if (!_presentationDiagnostics.Value) return;
            for (var page = 0; page < _frameSamples.Length; page++)
            {
                if (_frameSamples[page] < 30) continue;
                Logger.LogInfo("[RippersCasino] Presentation / " + PageNames[page] + " / focused frames=" + _frameSamples[page] + " / mean interval=" + (_frameTotals[page] / _frameSamples[page]).ToString("F2") + "ms / peak interval=" + _framePeaks[page].ToString("F2") + "ms / pooled graphics=" + (_lettering?.PooledCount ?? 0));
                _frameSamples[page] = 0;
                _frameTotals[page] = _framePeaks[page] = 0;
            }
        }
    }
}
