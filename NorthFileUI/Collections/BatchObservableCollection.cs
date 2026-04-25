using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NorthFileUI.Collections;

public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IReadOnlyList<T> items)
    {
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        RaiseReset();
    }

    public void Resize(int targetCount, Func<T> factory)
    {
        if (targetCount < 0)
        {
            targetCount = 0;
        }

        bool changed = false;
        while (Items.Count < targetCount)
        {
            Items.Add(factory());
            changed = true;
        }

        while (Items.Count > targetCount)
        {
            Items.RemoveAt(Items.Count - 1);
            changed = true;
        }

        if (changed)
        {
            RaiseReset();
        }
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
