using EFT.Hideout;
using EFT.UI;

namespace RippersCasino.Client
{
    internal static class StageInteractionRegistration
    {
        internal static void Ensure(Stage stage, InteractionAction action)
        {
            if (stage.Interactions == null) stage.Interactions = new RelatedInteractions();
            if (stage.Interactions.Data == null) stage.Interactions.Data = new AvailableInteractionState();
            var actions = stage.Interactions.Data.Actions;
            var found = false;
            for (var index = 0; index < actions.Count;)
            {
                if (actions[index].Name != action.Name) { index++; continue; }
                if (found) { actions.RemoveAt(index); continue; }
                if (!object.ReferenceEquals(actions[index], action)) actions[index] = action;
                index++;
                found = true;
            }
            if (!found) actions.Add(action);
        }
    }
}
