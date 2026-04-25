using System;
using System.Collections.Generic;

namespace NorthFileUI
{
    internal sealed class DirectorySessionController
    {
        public void ApplyPushHistoryIfNeeded(Stack<string> backStack, Stack<string> forwardStack, string currentPath, string targetPath, bool pushHistory)
        {
            if (!pushHistory || string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            backStack.Push(currentPath);
            forwardStack.Clear();
        }

        public bool TryGoBack(Stack<string> backStack, Stack<string> forwardStack, string currentPath, out string previousPath)
        {
            if (backStack.Count == 0)
            {
                previousPath = string.Empty;
                return false;
            }

            previousPath = backStack.Pop();
            forwardStack.Push(currentPath);
            return true;
        }

        public bool TryGoForward(Stack<string> backStack, Stack<string> forwardStack, string currentPath, out string nextPath)
        {
            if (forwardStack.Count == 0)
            {
                nextPath = string.Empty;
                return false;
            }

            nextPath = forwardStack.Pop();
            backStack.Push(currentPath);
            return true;
        }
    }
}
