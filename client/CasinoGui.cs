using System;
using UnityEngine;

namespace RippersCasino.Client
{
    internal sealed class CasinoGui : MonoBehaviour
    {
        internal Action Draw;
        private void OnGUI() => Draw?.Invoke();
    }
}
