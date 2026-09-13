using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Application;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.UI;

public static class UiStateMapper
{
    public static string MapSessionState(GameSessionState state) => state switch
    {
        GameSessionState.Disabled => "Désactivé",
        GameSessionState.Ready => "Prêt",
        GameSessionState.Active => "Actif",
        GameSessionState.Paused => "Pause",
        GameSessionState.MaxLimit => "Limite maximale",
        GameSessionState.Restoring => "Restauration",
        GameSessionState.Error => "Erreur",
        _ => "Erreur"
    };

    public static string MapAutoTdpState(AutoTdpState state) => state switch
    {
        AutoTdpState.MaxLimit or AutoTdpState.SafetyClamped =>
            "Limite maximale",
        AutoTdpState.FpsInvalid => "Pause",
        AutoTdpState.FpsSourceLost or
            AutoTdpState.PowerInvalid or
            AutoTdpState.Faulted => "Erreur",
        AutoTdpState.Restored => "Restauration",
        _ => "Actif"
    };

    public static string MapPowerSource(PowerSourceKind source) => source switch
    {
        PowerSourceKind.Battery => "Batterie",
        PowerSourceKind.Ac => "Secteur",
        _ => "Inconnue"
    };

    public static string MapRestoreMode(RestoreMode mode) => mode switch
    {
        RestoreMode.Silent => "Silencieux",
        RestoreMode.Balanced => "Performance",
        RestoreMode.Turbo => "Turbo",
        _ => "Invalide"
    };
}
