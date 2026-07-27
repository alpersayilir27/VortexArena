using System;

namespace VortexArena.Protocol
{
    // Tüm WS kontrol DTO'ları (Docs/ArenaNet-Protokol.md §5) + UDP beacon DTO'su (§4).
    // Kurallar: [Serializable], yalnız public alan, Dictionary/polimorfizm yok,
    // alan adları protokol dokümanındaki camelCase ile birebir.

    /// Zarf: alıcı önce yalnız type'ı parse eder, sonra tam DTO'ya geçer.
    [Serializable]
    public class MsgEnvelope
    {
        public string type;
    }

    // ---- İstemci → Sunucu ----

    [Serializable]
    public class HelloMsg
    {
        public string type = MessageTypes.Hello;
        public int protocolVersion;
        public string role;
        public string deviceId;
        public string deviceName;
        public string appVersion;
        public string currentScene;
        public string[] scenes;
    }

    [Serializable]
    public class StatusMsg
    {
        public string type = MessageTypes.Status;
        public string scene;
        public float battery;
        public float fps;
    }

    [Serializable]
    public class SetNameMsg
    {
        public string type = MessageTypes.SetName;
        public string name;
    }

    [Serializable]
    public class SetReadyMsg
    {
        public string type = MessageTypes.SetReady;
        public bool ready;
    }

    /// İstemci → sunucu yönünde seq dolu; sunucu relay'inde playerId dolu (seq taşınmaz).
    [Serializable]
    public class ShotFiredMsg
    {
        public string type = MessageTypes.ShotFired;
        public int seq;
        public int playerId;
        public string weaponId;
        public float[] muzzlePos;
        public float[] muzzleDir;
    }

    [Serializable]
    public class HitReportMsg
    {
        public string type = MessageTypes.HitReport;
        public int seq;
        public int targetPlayerId;
        public string weaponId;
        public float damage;
        public float[] hitPos;
    }

    /// Ölü oyuncunun canlanma talebi (§10.4): respawn.delaySeconds dolmuş VE oyuncu kendi
    /// tabanındayken gönderilir; sunucu koşulları doğrulayıp canlandırır. Alan taşımaz.
    [Serializable]
    public class ReviveRequestMsg
    {
        public string type = MessageTypes.ReviveRequest;
    }

    // ---- Yalnız admin → Sunucu ----

    [Serializable]
    public class StartMatchMsg
    {
        public string type = MessageTypes.StartMatch;
        public string modeId;
        public string sceneName;
    }

    [Serializable]
    public class AbortMatchMsg
    {
        public string type = MessageTypes.AbortMatch;
    }

    [Serializable]
    public class SetTeamMsg
    {
        public string type = MessageTypes.SetTeam;
        public int playerId;
        public string team;
    }

    [Serializable]
    public class KickMsg
    {
        public string type = MessageTypes.Kick;
        public int playerId;
    }

    /// Admin → sunucu yönünde playerId dolu; sunucu → istemci yönünde alansız gider.
    [Serializable]
    public class IdentifyMsg
    {
        public string type = MessageTypes.Identify;
        public int playerId;
    }

    /// Bir sonraki maçın ORTAK mod/harita seçimi (§5.2). Maçı başlatmaz; sunucudaki seçimi
    /// günceller ve sunucu onu admin_state ile tüm adminlere yayar. Boş alan mevcut değeri korur.
    [Serializable]
    public class SetSelectionMsg
    {
        public string type = MessageTypes.SetSelection;
        public string modeId;
        public string sceneName;
    }

    // ---- Sunucu → İstemci ----

    [Serializable]
    public class MatchInfo
    {
        public string phase;
        public string modeId;
        public string sceneName;
        public float timeRemaining;
        public int scoreRed;
        public int scoreBlue;
    }

    [Serializable]
    public class WelcomeMsg
    {
        public string type = MessageTypes.Welcome;
        public int protocolVersion;
        public int playerId;
        public uint udpToken;
        public MatchInfo match;
    }

    [Serializable]
    public class PlayerInfo
    {
        public int playerId;
        public string name;
        public string role;
        public string team;
        public bool ready;
        public bool online;
        public float battery;
        public string scene;

        // Maç sayaçları (§10.2) — SUNUCU-OTORİTER, admin gözlemci arayüzünün doğruluk kaynağı.
        // Yalnız kill_event/health_update sayılsa admin yeniden bağlandığında tablo sıfırlanırdı.
        // Lobby fazında: hp=PLAYER_MAX_HP, alive=true, sayaçlar 0. Oyuncu istemcisi yok sayar.
        public int kills;
        public int deaths;
        public float hp;
        public bool alive;
    }

    [Serializable]
    public class LobbyStateMsg
    {
        public string type = MessageTypes.LobbyState;
        public PlayerInfo[] players;
    }

    [Serializable]
    public class LoadMatchMsg
    {
        public string type = MessageTypes.LoadMatch;
        public string modeId;
        public string sceneName;
        public int roundSeconds;
        public int scoreLimit;
        public string yourTeam;
        public int spawnSlot;
    }

    [Serializable]
    public class CountdownMsg
    {
        public string type = MessageTypes.Countdown;
        public int seconds;
    }

    [Serializable]
    public class MatchStateMsg
    {
        public string type = MessageTypes.MatchState;
        public string phase;
        public float timeRemaining;
        public int scoreRed;
        public int scoreBlue;
    }

    [Serializable]
    public class HealthUpdateMsg
    {
        public string type = MessageTypes.HealthUpdate;
        public int playerId;
        public float hp;
        public int attackerId;
    }

    [Serializable]
    public class KillEventMsg
    {
        public string type = MessageTypes.KillEvent;
        public int killerId;
        public int victimId;
        public string weaponId;
    }

    [Serializable]
    public class RespawnMsg
    {
        public string type = MessageTypes.Respawn;
        public int playerId;
        public int spawnSlot;
        public float delaySeconds;
    }

    [Serializable]
    public class MatchEndMsg
    {
        public string type = MessageTypes.MatchEnd;
        public string winnerTeam;
        public int scoreRed;
        public int scoreBlue;
    }

    [Serializable]
    public class ReturnToLobbyMsg
    {
        public string type = MessageTypes.ReturnToLobby;
    }

    [Serializable]
    public class PingMsg
    {
        public string type = MessageTypes.Ping;
    }

    [Serializable]
    public class KickedMsg
    {
        public string type = MessageTypes.Kicked;
        public string reason;
    }

    /// Yalnız role=admin bağlantılara (§5.3): adminler arası ORTAK durumun tek doğruluk kaynağı.
    /// modeId/sceneName ortak seçimdir (arayüz kendi yerelini değil bunu gösterir); notice son
    /// admin eyleminin "<ad>: <eylem>" özetidir; adminCount çevrimiçi admin sayısıdır.
    /// Görünüm tercihleri (kamera, halka, saydamlık…) BURAYA GİRMEZ — her admin'in kendi ekranı.
    [Serializable]
    public class AdminStateMsg
    {
        public string type = MessageTypes.AdminState;
        public string modeId;
        public string sceneName;
        public string notice;
        public int adminCount;
    }

    // ---- UDP beacon (§4; WS mesajı değildir, alıcı app alanını doğrular) ----

    [Serializable]
    public class BeaconMsg
    {
        public string app;
        public int ver;
        public string ip;
        public int controlPort;
        public int statePort;
        public string serverId;
    }
}
