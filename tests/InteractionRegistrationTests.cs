using EFT.Hideout;
using EFT.UI;
using RippersCasino.Client;

internal static class InteractionRegistrationTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Action<Stage, InteractionAction> verify) => test("Intel interaction / " + name, () =>
        {
            var stage = new Stage();
            var action = new InteractionAction { Name = "Rippers Casino" };
            verify(stage, action);
        });
        Check("initial registration creates the native interaction state", (stage, action) =>
        {
            StageInteractionRegistration.Ensure(stage, action);
            Require(stage.Interactions!.Data!.Actions.Single() == action);
        });
        Check("repeated binding never creates duplicate casino actions", (stage, action) =>
        {
            for (var count = 0; count < 1000; count++) StageInteractionRegistration.Ensure(stage, action);
            Require(stage.Interactions!.Data!.Actions.Single() == action);
        });
        Check("a cleared action list is repaired for the same stage instance", (stage, action) =>
        {
            StageInteractionRegistration.Ensure(stage, action);
            stage.Interactions!.Data!.Actions.Clear();
            StageInteractionRegistration.Ensure(stage, action);
            Require(stage.Interactions.Data.Actions.Single() == action);
        });
        Check("replacement state receives a working casino callback", (stage, action) =>
        {
            var opened = 0; action.Action = () => opened++;
            StageInteractionRegistration.Ensure(stage, action);
            stage.Interactions!.Data = new AvailableInteractionState();
            StageInteractionRegistration.Ensure(stage, action);
            stage.Interactions.Data.Actions.Single().Action!();
            Require(opened == 1);
        });
        Check("replacement interaction container is repaired without touching the old list", (stage, action) =>
        {
            StageInteractionRegistration.Ensure(stage, action);
            var previous = stage.Interactions!.Data!;
            stage.Interactions = new RelatedInteractions();
            StageInteractionRegistration.Ensure(stage, action);
            Require(stage.Interactions.Data!.Actions.Single() == action && previous.Actions.Single() == action);
        });
        Check("missing replacement data is recreated on binding", (stage, action) =>
        {
            StageInteractionRegistration.Ensure(stage, action);
            stage.Interactions!.Data = null;
            StageInteractionRegistration.Ensure(stage, action);
            Require(stage.Interactions.Data!.Actions.Single() == action);
        });
        Check("a copied name-only action is replaced with the live callback", (stage, action) =>
        {
            var opened = false; action.Action = () => opened = true;
            stage.Interactions = new RelatedInteractions { Data = new AvailableInteractionState() };
            stage.Interactions.Data.Actions.Add(new InteractionAction { Name = action.Name });
            StageInteractionRegistration.Ensure(stage, action);
            stage.Interactions.Data.Actions.Single().Action!();
            Require(opened && stage.Interactions.Data.Actions.Single() == action);
        });
        Check("other native actions retain order and callbacks while duplicates are removed", (stage, action) =>
        {
            var before = new InteractionAction { Name = "Before" };
            var after = new InteractionAction { Name = "After" };
            stage.Interactions = new RelatedInteractions { Data = new AvailableInteractionState() };
            stage.Interactions.Data.Actions.AddRange([before, new() { Name = action.Name }, action, after]);
            StageInteractionRegistration.Ensure(stage, action);
            Require(stage.Interactions.Data.Actions.SequenceEqual([before, action, after]));
        });
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new Exception("Live stage interaction registration is incorrect.");
    }
}

// These stand-ins isolate the production list-registration code from Unity's native runtime.
namespace EFT.UI
{
    internal sealed class InteractionAction
    {
        public string Name = "";
        public Action? Action;
    }
    internal sealed class AvailableInteractionState
    {
        public List<InteractionAction> Actions = [];
    }
}
namespace EFT.Hideout
{
    internal sealed class Stage
    {
        public RelatedInteractions? Interactions;
    }
    internal sealed class RelatedInteractions
    {
        public EFT.UI.AvailableInteractionState? Data;
    }
}
