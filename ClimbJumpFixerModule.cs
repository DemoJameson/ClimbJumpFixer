using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.ClimbJumpFixer {
    public class ClimbJumpFixerModule : EverestModule {
        public static ClimbJumpFixerModule Instance { get; private set; }
        public static ClimbJumpFixerSettings Settings => (ClimbJumpFixerSettings) Instance._Settings;
        public override Type SettingsType => typeof(ClimbJumpFixerSettings);

        private static readonly FieldInfo JumpGraceTimer = typeof(Player).GetField("jumpGraceTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo WallJumpCheck = typeof(Player).GetMethod("WallJumpCheck", BindingFlags.Instance | BindingFlags.NonPublic);

        private static ILHook ilHook;

        public ClimbJumpFixerModule() {
            Instance = this;
        }

        public override void Load() {
            IL.Celeste.Player.NormalUpdate += PlayerOnNormalUpdate;
            IL.Celeste.Player.ClimbUpdate += ModCanUnDuck;
            IL.Celeste.Player.NormalUpdate += ModCanUnDuck;
            IL.Celeste.Player.DashUpdate += ModCanUnDuck;
            IL.Celeste.Player.SuperWallJump += ModDucking;
            ilHook = new ILHook(typeof(Player).GetMethod("orig_WallJump", BindingFlags.Instance | BindingFlags.NonPublic), ModDucking);
        }

        public override void Unload() {
            IL.Celeste.Player.NormalUpdate -= PlayerOnNormalUpdate;
            IL.Celeste.Player.ClimbUpdate -= ModCanUnDuck;
            IL.Celeste.Player.NormalUpdate -= ModCanUnDuck;
            IL.Celeste.Player.DashUpdate -= ModCanUnDuck;
            IL.Celeste.Player.SuperWallJump -= ModDucking;
            ilHook.Dispose();
        }

        private void ModCanUnDuck(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            while (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.MatchCallvirt<Player>("get_CanUnDuck")
            )) {
                ilCursor.EmitDelegate<Func<bool, bool>>(canUnDuck => Settings.DuckableWallJump || canUnDuck);
            }
        }

        private void ModDucking(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(
                MoveType.After,
                ins => ins.OpCode == OpCodes.Ldarg_0,
                ins => ins.OpCode == OpCodes.Ldc_I4_0,
                ins => ins.MatchCallvirt<Player>("set_Ducking")
            )) {
                ilCursor.Index--;
                ilCursor.Emit(OpCodes.Ldarg_0).EmitDelegate<Func<bool, Player, bool>>((duck, player) => {
                    if (Settings.DuckableWallJump && !player.CanUnDuck) {
                        Engine.Commands.Log("Jump!");
                    }
                    return Settings.DuckableWallJump && !player.CanUnDuck || duck;
                });
            }
        }

        private void PlayerOnNormalUpdate(ILContext il) {
            ILCursor ilCursor = new ILCursor(il);
            if (ilCursor.TryGotoNext(MoveType.After, instruction => instruction.MatchLdsfld(typeof(Input), "Grab"),
                instruction => instruction.MatchCallvirt<VirtualButton>("get_Check")
            )) {
                if (ilCursor.TryGotoNext(MoveType.After, instruction => instruction.OpCode == OpCodes.Ldarg_0,
                    instruction => instruction.MatchLdflda<Player>("Speed"),
                    instruction => instruction.MatchLdfld<Vector2>("Y"))
                ) {
                    ilCursor.Emit(OpCodes.Ldarg_0);
                    ilCursor.EmitDelegate<Func<Player, float, float>>(delegate(Player player, float speedY) {
                        if (ClimbJumpCheck(player)) {
                            if (Input.Jump.Pressed && Math.Sign(player.Speed.X) != -(int) player.Facing && player.ClimbCheck((int)player.Facing)) {
                                Level level = player.SceneAs<Level>();
                                Logger.Log("ClimbJumpFixer", $"Maybe try to grab but climbjump: chapter time {TimeSpan.FromTicks(level.Session.Time).ShortGameplayFormat()}");
                            }
                            return -1f;
                        } else {
                            return speedY;
                        }
                    });
                }
            }
        }

        private bool ClimbJumpCheck(Player player) {
            return Settings.FixFallingClimbJump && Input.Jump.Pressed && (float) JumpGraceTimer.GetValue(player) <= 0f && player.CanUnDuck &&
                   (bool) WallJumpCheck.Invoke(player, new object[] {(int) player.Facing});
        }
    }
}