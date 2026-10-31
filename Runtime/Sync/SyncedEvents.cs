using System;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Sync
{
    // Unity cannot serialize UnityEvent<T> directly; concrete subclasses are required
    // for the Inspector to show a usable event list.
    [Serializable] public class BoolEvent : UnityEvent<bool> { }
    [Serializable] public class IntEvent : UnityEvent<int> { }
    [Serializable] public class FloatEvent : UnityEvent<float> { }
}
