using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FumblePeak.EditorTools
{
    /// <summary>
    /// Fumble Peak metinlerini NextLocalization tablosuna (Türkçe / English / Deutsch) yazar.
    /// Var olan anahtarların değerleri güncellenir, başka anahtarlara dokunulmaz.
    /// Menü: FumblePeak → Kurulum → Çevirileri Yaz
    /// Not: Çevirileri sonradan NextHaven Studio → Localization Editor'dan düzenleyebilirsin;
    /// bu komutu tekrar çalıştırırsan buradaki değerler üzerine yazılır.
    /// </summary>
    public static class FumbleLocalizationSeeder
    {
        const string LocDir = "Assets/Package/NextLocalization/Resources/Localization";
        const string TablePath = LocDir + "/LocalizationTable.asset";

        // key, Türkçe, English, Deutsch
        static readonly string[][] S =
        {
            // ---------- Giriş ----------
            new[] { "intro.top", "1 BEDEN · 4 UZUV · 0 KOORDİNASYON", "1 BODY · 4 LIMBS · 0 COORDINATION", "1 KÖRPER · 4 GLIEDMASSEN · 0 KOORDINATION" },
            new[] { "intro.tagline", "Biri erken bırakırsa, hepimiz düşeriz.", "If one lets go too early, we all fall.", "Lässt einer zu früh los, fallen wir alle." },
            new[] { "intro.press", "DEVAM ETMEK İÇİN BİR TUŞA BAS", "PRESS ANY KEY", "BELIEBIGE TASTE DRÜCKEN" },

            // ---------- Ana menü ----------
            new[] { "menu.single", "TEK OYUNCU", "SINGLEPLAYER", "EINZELSPIELER" },
            new[] { "menu.hic", "Hık!", "Hic!", "Hicks!" },
            new[] { "menu.burp", "Böğğrk!", "Buuurp!", "Rülps!" },
            new[] { "menu.nature", "Doğa çağırıyor...", "Nature calls...", "Die Natur ruft..." },
            new[] { "menu.ahh", "Ohhh, oh be...", "Ahhh, that's the stuff...", "Ahhh, herrlich..." },
            new[] { "menu.scream", "AAAAAAA!", "WAAAAAH!", "AAAAAAH!" },
            new[] { "menu.single_desc", "Ahtapot modu: dört uzuv da sende!", "Octopus mode: all four limbs are yours!", "Oktopus-Modus: alle vier Gliedmaßen gehören dir!" },
            new[] { "menu.multi", "ÇOK OYUNCULU", "MULTIPLAYER", "MEHRSPIELER" },
            new[] { "menu.multi_desc", "2-4 kişilik co-op. Lobi kur ya da arkadaşına katıl.", "2-4 player co-op. Host a lobby or join a friend.", "Koop für 2-4. Lobby erstellen oder Freunden beitreten." },
            new[] { "menu.settings", "AYARLAR", "SETTINGS", "EINSTELLUNGEN" },
            new[] { "menu.quit", "ÇIKIŞ", "QUIT", "BEENDEN" },
            new[] { "menu.back", "← GERİ", "← BACK", "← ZURÜCK" },
            new[] { "menu.steam_ok", "Steam bağlı · {0}", "Steam connected · {0}", "Steam verbunden · {0}" },
            new[] { "menu.steam_off", "Steam bulunamadı · Yerel ağ modu", "Steam not found · Local network mode", "Steam nicht gefunden · LAN-Modus" },

            // ---------- Nasıl oynanır ----------
            new[] { "howto.title", "NASIL OYNANIR", "HOW TO PLAY", "SO WIRD GESPIELT" },
            new[] { "howto.l1", "Herkes bedenin bir uzvunu yönetir.", "Everyone controls one limb of the body.", "Jeder steuert eine Gliedmaße des Körpers." },
            new[] { "howto.l2", "FARE — elini / ayağını hareket ettir", "MOUSE — move your hand / foot", "MAUS — Hand / Fuß bewegen" },
            new[] { "howto.l3", "SOL TIK — imlecin altındaki kayaya tutun", "LEFT CLICK — grab the rock under the cursor", "LINKSKLICK — den Stein unter dem Zeiger greifen" },
            new[] { "howto.l4", "Tutununca kontrol diğer ele geçer: yukarı uzan, tıkla, tekrarla!", "After grabbing, control passes to the other hand: reach up, click, repeat!", "Nach dem Greifen wechselt die Steuerung zur anderen Hand: hoch, klicken, wiederholen!" },
            new[] { "howto.l5", "Q / E / A / D — uzuv seç  ·  SAĞ TIK — bırak", "Q / E / A / D — select limb  ·  RIGHT CLICK — let go", "Q / E / A / D — Gliedmaße wählen  ·  RECHTSKLICK — loslassen" },
            new[] { "howto.l6", "Tutunan el seni kendiliğinden yukarı çeker. Parlayan kaya = tutunabileceğin yer.", "The holding hand pulls you up by itself. Glowing rock = where you can grab.", "Die haltende Hand zieht dich von selbst hoch. Leuchtender Stein = greifbar." },
            new[] { "howto.l7", "Tek kolla uzun süre asılı kalma: kollar kızarır, titrer ve bırakır!", "Don't hang on one arm too long: arms redden, shake and let go!", "Nicht zu lange an einem Arm hängen: Arme werden rot, zittern und lassen los!" },
            new[] { "howto.players", "OYUNCU DAĞILIMI", "PLAYER SPLIT", "SPIELERAUFTEILUNG" },
            new[] { "howto.p1", "1 — Ahtapot modu (4 uzuv)", "1 — Octopus mode (4 limbs)", "1 — Oktopus-Modus (4 Gliedmaßen)" },
            new[] { "howto.p2", "2 — Çapraz bağ (kol + karşı bacak)", "2 — Cross link (arm + opposite leg)", "2 — Kreuzverbindung (Arm + Gegenbein)" },
            new[] { "howto.p3", "3 — Asimetrik (biri iki bacak)", "3 — Asymmetric (one gets both legs)", "3 — Asymmetrisch (einer hat beide Beine)" },
            new[] { "howto.p4", "4 — Tam kaos (herkese bir uzuv)", "4 — Total chaos (one limb each)", "4 — Totales Chaos (je eine Gliedmaße)" },

            // ---------- Çok oyunculu ----------
            new[] { "mp.title", "ÇOK OYUNCULU", "MULTIPLAYER", "MEHRSPIELER" },
            new[] { "mp.host", "LOBİ KUR", "HOST LOBBY", "LOBBY ERSTELLEN" },
            new[] { "mp.host_steam", "STEAM LOBİSİ KUR", "HOST STEAM LOBBY", "STEAM-LOBBY ERSTELLEN" },
            new[] { "mp.privacy", "Gizlilik", "Privacy", "Sichtbarkeit" },
            new[] { "mp.privacy_friends", "Arkadaşlar", "Friends only", "Nur Freunde" },
            new[] { "mp.privacy_public", "Herkese açık", "Public", "Öffentlich" },
            new[] { "mp.privacy_invite", "Sadece davet", "Invite only", "Nur Einladung" },
            new[] { "mp.section_host", "LOBİ KUR", "HOST", "HOSTEN" },
            new[] { "mp.section_join", "KATIL", "JOIN", "BEITRETEN" },
            new[] { "mp.code", "Lobi kodu", "Lobby code", "Lobby-Code" },
            new[] { "mp.code_join", "KOD İLE KATIL", "JOIN WITH CODE", "MIT CODE BEITRETEN" },
            new[] { "mp.section_lan", "YEREL AĞ / IP", "LOCAL NETWORK / IP", "LOKALES NETZ / IP" },
            new[] { "mp.address", "Adres", "Address", "Adresse" },
            new[] { "mp.lan_host", "IP İLE LOBİ KUR", "HOST VIA IP", "ÜBER IP HOSTEN" },
            new[] { "mp.lan_join", "IP İLE BAĞLAN", "CONNECT VIA IP", "ÜBER IP VERBINDEN" },
            new[] { "mp.lan_hint", "Aynı ağdaysanız host'un yerel IP'sini girin. İnternet üzerinden host UDP {0} portunu açmalı.", "On the same network, enter the host's local IP. Over the internet the host must open UDP port {0}.", "Im selben Netz die lokale IP des Hosts eingeben. Über das Internet muss der Host UDP-Port {0} öffnen." },
            new[] { "mp.my_ip", "Senin yerel IP'n: {0}", "Your local IP: {0}", "Deine lokale IP: {0}" },
            new[] { "mp.tab_friends", "ARKADAŞLAR", "FRIENDS", "FREUNDE" },
            new[] { "mp.tab_public", "AÇIK LOBİLER", "PUBLIC LOBBIES", "ÖFFENTLICHE LOBBYS" },
            new[] { "mp.friends_empty", "Şu an Fumble Peak oynayan arkadaşın yok.", "None of your friends are playing Fumble Peak right now.", "Gerade spielt keiner deiner Freunde Fumble Peak." },
            new[] { "mp.public_empty", "Açık lobi bulunamadı.", "No public lobbies found.", "Keine öffentlichen Lobbys gefunden." },
            new[] { "mp.nosteam", "Steam açık değil. Arkadaş listesi ve davetler için Steam'i başlat.", "Steam is not running. Start Steam for friends and invites.", "Steam läuft nicht. Starte Steam für Freunde und Einladungen." },
            new[] { "mp.refresh", "YENİLE", "REFRESH", "AKTUALISIEREN" },
            new[] { "mp.join", "KATIL", "JOIN", "BEITRETEN" },
            new[] { "mp.friend_in_lobby", "Lobide", "In a lobby", "In einer Lobby" },
            new[] { "mp.friend_playing", "Oynuyor", "Playing", "Spielt" },
            new[] { "mp.players", "{0}/{1} oyuncu", "{0}/{1} players", "{0}/{1} Spieler" },
            new[] { "mp.loading", "Yükleniyor...", "Loading...", "Lädt..." },

            // ---------- Lobi ----------
            new[] { "lobby.title", "LOBİ", "LOBBY", "LOBBY" },
            new[] { "lobby.code", "Lobi kodu", "Lobby code", "Lobby-Code" },
            new[] { "lobby.address", "Adres", "Address", "Adresse" },
            new[] { "lobby.offline", "Yerel oturum", "Local session", "Lokale Sitzung" },
            new[] { "lobby.copy", "KOPYALA", "COPY", "KOPIEREN" },
            new[] { "lobby.copied", "Kopyalandı!", "Copied!", "Kopiert!" },
            new[] { "lobby.invite", "ARKADAŞ DAVET ET", "INVITE FRIENDS", "FREUNDE EINLADEN" },
            new[] { "lobby.ready", "HAZIRIM", "READY", "BEREIT" },
            new[] { "lobby.unready", "HAZIR DEĞİLİM", "NOT READY", "NICHT BEREIT" },
            new[] { "lobby.start", "OYUNU BAŞLAT", "START GAME", "SPIEL STARTEN" },
            new[] { "lobby.leave", "LOBİDEN AYRIL", "LEAVE LOBBY", "LOBBY VERLASSEN" },
            new[] { "lobby.empty", "Boş yer", "Empty slot", "Freier Platz" },
            new[] { "lobby.empty_hint", "Arkadaşını davet et", "Invite a friend", "Lade einen Freund ein" },
            new[] { "lobby.host", "HOST", "HOST", "HOST" },
            new[] { "lobby.you", "(sen)", "(you)", "(du)" },
            new[] { "lobby.state_ready", "HAZIR", "READY", "BEREIT" },
            new[] { "lobby.state_waiting", "BEKLİYOR", "WAITING", "WARTET" },
            new[] { "lobby.kick", "AT", "KICK", "KICKEN" },
            new[] { "lobby.wait_players", "Oyuncuların hazır olması bekleniyor...", "Waiting for players to get ready...", "Warte, bis alle bereit sind..." },
            new[] { "lobby.all_ready", "Herkes hazır! Başlatabilirsin.", "Everyone is ready! You can start.", "Alle bereit! Du kannst starten." },
            new[] { "lobby.wait_host", "Host'un oyunu başlatması bekleniyor...", "Waiting for the host to start...", "Warte auf den Host..." },
            new[] { "lobby.mode", "Mod: {0}", "Mode: {0}", "Modus: {0}" },
            new[] { "lobby.mode1", "Ahtapot modu", "Octopus mode", "Oktopus-Modus" },
            new[] { "lobby.mode2", "Çapraz bağ", "Cross link", "Kreuzverbindung" },
            new[] { "lobby.mode3", "Asimetrik", "Asymmetric", "Asymmetrisch" },
            new[] { "lobby.mode4", "Tam kaos", "Total chaos", "Totales Chaos" },
            new[] { "lobby.joined", "{0} lobiye katıldı", "{0} joined", "{0} ist beigetreten" },
            new[] { "lobby.left", "{0} ayrıldı", "{0} left", "{0} hat verlassen" },

            // ---------- Ayarlar ----------
            new[] { "settings.title", "AYARLAR", "SETTINGS", "EINSTELLUNGEN" },
            new[] { "settings.sec_game", "OYUN", "GAME", "SPIEL" },
            new[] { "settings.sec_video", "GÖRÜNTÜ", "VIDEO", "GRAFIK" },
            new[] { "settings.sec_audio", "SES", "AUDIO", "AUDIO" },
            new[] { "settings.language", "Dil", "Language", "Sprache" },
            new[] { "settings.name", "Oyuncu adı", "Player name", "Spielername" },
            new[] { "settings.name_steam", "Steam adın kullanılıyor.", "Your Steam name is used.", "Dein Steam-Name wird verwendet." },
            new[] { "settings.sens", "Fare hassasiyeti", "Mouse sensitivity", "Mausempfindlichkeit" },
            new[] { "settings.volume", "Ana ses", "Master volume", "Gesamtlautstärke" },
            new[] { "settings.fullscreen", "Tam ekran", "Fullscreen", "Vollbild" },
            new[] { "settings.vsync", "Dikey senkronizasyon (V-Sync)", "V-Sync", "V-Sync" },
            new[] { "settings.quality", "Grafik kalitesi", "Graphics quality", "Grafikqualität" },
            new[] { "settings.resolution", "Çözünürlük", "Resolution", "Auflösung" },

            // ---------- Mesajlar ----------
            new[] { "msg.connecting", "BAĞLANILIYOR", "CONNECTING", "VERBINDE" },
            new[] { "msg.connecting_to", "{0} adresine bağlanılıyor...", "Connecting to {0}...", "Verbinde mit {0}..." },
            new[] { "msg.creating", "LOBİ KURULUYOR", "CREATING LOBBY", "LOBBY WIRD ERSTELLT" },
            new[] { "msg.creating_steam", "Steam lobisi hazırlanıyor...", "Preparing Steam lobby...", "Steam-Lobby wird vorbereitet..." },
            new[] { "msg.joining", "Lobiye katılınıyor...", "Joining lobby...", "Trete Lobby bei..." },
            new[] { "msg.cancel", "İPTAL", "CANCEL", "ABBRECHEN" },
            new[] { "msg.ok", "TAMAM", "OK", "OK" },
            new[] { "msg.yes", "EVET", "YES", "JA" },
            new[] { "msg.no", "HAYIR", "NO", "NEIN" },
            new[] { "msg.failed", "BAĞLANTI KURULAMADI", "CONNECTION FAILED", "VERBINDUNG FEHLGESCHLAGEN" },
            new[] { "msg.unreachable", "Sunucuya ulaşılamadı.", "Could not reach the server.", "Server nicht erreichbar." },
            new[] { "msg.disconnected", "Bağlantı koptu ya da host oturumu kapattı.", "Disconnected, or the host closed the session.", "Verbindung getrennt oder Host hat beendet." },
            new[] { "msg.kicked", "Host seni lobiden attı.", "You were kicked by the host.", "Du wurdest vom Host gekickt." },
            new[] { "msg.full", "Lobi dolu (4/4).", "Lobby is full (4/4).", "Lobby ist voll (4/4)." },
            new[] { "msg.not_found", "Lobi bulunamadı ya da kapanmış.", "Lobby not found or closed.", "Lobby nicht gefunden oder geschlossen." },
            new[] { "msg.create_failed", "Steam lobisi oluşturulamadı ({0}).", "Could not create Steam lobby ({0}).", "Steam-Lobby konnte nicht erstellt werden ({0})." },
            new[] { "msg.version", "Farklı oyun sürümü (host: {0}, sen: {1}).", "Version mismatch (host: {0}, you: {1}).", "Versionskonflikt (Host: {0}, du: {1})." },
            new[] { "msg.invalid_code", "Geçersiz lobi kodu. Kod sadece rakamlardan oluşur.", "Invalid lobby code. It only contains digits.", "Ungültiger Lobby-Code. Nur Ziffern erlaubt." },
            new[] { "msg.connection", "BAĞLANTI", "CONNECTION", "VERBINDUNG" },
            new[] { "msg.quit_title", "ÇIKIŞ", "QUIT", "BEENDEN" },
            new[] { "msg.quit_confirm", "Oyundan çıkmak istiyor musun?", "Do you want to quit the game?", "Willst du das Spiel beenden?" },
            new[] { "msg.leave_title", "LOBİDEN AYRIL", "LEAVE LOBBY", "LOBBY VERLASSEN" },
            new[] { "msg.leave_host", "Sen host'sun: ayrılırsan lobi herkes için kapanır.", "You are the host: leaving closes the lobby for everyone.", "Du bist der Host: Wenn du gehst, schließt die Lobby für alle." },
            new[] { "msg.leave_client", "Lobiden ayrılmak istiyor musun?", "Do you want to leave the lobby?", "Willst du die Lobby verlassen?" },
            new[] { "msg.get_ready", "ZİRVEYE HAZIRLAN", "GET READY TO CLIMB", "MACH DICH BEREIT" },
            new[] { "msg.octopus", "AHTAPOT MODU", "OCTOPUS MODE", "OKTOPUS-MODUS" },

            // ---------- Oyun içi ----------
            new[] { "hud.biome1", "BAŞLANGIÇ VADİSİ", "STARTER VALLEY", "STARTTAL" },
            new[] { "hud.controls", "FARE uzan  ·  SOL TIK tutun  ·  SAĞ TIK bırak  ·  Q/E/A/D uzuv seç  ·  H gizle  ·  ESC {0}", "MOUSE reach  ·  LEFT CLICK grab  ·  RIGHT CLICK let go  ·  Q/E/A/D select limb  ·  H hide  ·  ESC {0}", "MAUS greifen  ·  LINKSKLICK festhalten  ·  RECHTSKLICK loslassen  ·  Q/E/A/D wählen  ·  H ausblenden  ·  ESC {0}" },
            new[] { "hud.esc_pause", "duraklat", "pause", "Pause" },
            new[] { "hud.esc_menu", "menü", "menu", "Menü" },
            new[] { "hud.paused", "DURAKLATILDI", "PAUSED", "PAUSIERT" },
            new[] { "hud.paused_single", "Oyun duraklatıldı.", "The game is paused.", "Das Spiel ist pausiert." },
            new[] { "hud.paused_coop", "(Oyun arka planda devam ediyor — arkadaşların hâlâ asılı!)", "(The game keeps running — your friends are still hanging!)", "(Das Spiel läuft weiter — deine Freunde hängen noch!)" },
            new[] { "hud.resume", "DEVAM ET", "RESUME", "FORTSETZEN" },
            new[] { "hud.respawn", "KAMPA DÖN", "BACK TO CAMP", "ZURÜCK ZUM LAGER" },
            new[] { "hud.to_lobby", "LOBİYE DÖN (HERKES)", "BACK TO LOBBY (ALL)", "ZURÜCK ZUR LOBBY (ALLE)" },
            new[] { "hud.to_menu", "ANA MENÜYE DÖN", "MAIN MENU", "HAUPTMENÜ" },
            new[] { "hud.camp", "KAMP ATEŞİ! Yeni dönüş noktası", "CAMPFIRE! New checkpoint", "LAGERFEUER! Neuer Checkpoint" },
            new[] { "hud.respawned", "Kampa geri dönüldü", "Back to camp", "Zurück im Lager" },
            new[] { "hud.gave_up", "{0} pes etti!", "{0} gave up!", "{0} gibt auf!" },
            new[] { "hud.summit", "ZİRVE!", "SUMMIT!", "GIPFEL!" },
            new[] { "hud.summit_text", "Kimse kimseyi bırakmadı. (Pek sayılmaz.)", "Nobody let anyone go. (Mostly.)", "Niemand hat losgelassen. (Fast.)" },
            new[] { "hud.state_grab", "TUTUYOR", "HOLDING", "HÄLT" },
            new[] { "hud.state_fist", "YUMRUK", "FIST", "FAUST" },
            new[] { "hud.state_free", "SERBEST", "FREE", "FREI" },
            new[] { "hud.address", "Adres (port {0}):", "Address (port {0}):", "Adresse (Port {0}):" },
            new[] { "hud.connected", "Bağlı:", "Connected:", "Verbunden:" },

            // ---------- Uzuvlar ----------
            new[] { "limb.0", "Sol Kol", "Left Arm", "Linker Arm" },
            new[] { "limb.1", "Sağ Kol", "Right Arm", "Rechter Arm" },
            new[] { "limb.2", "Sol Bacak", "Left Leg", "Linkes Bein" },
            new[] { "limb.3", "Sağ Bacak", "Right Leg", "Rechtes Bein" },
            new[] { "limb.none", "İzleyici", "Spectator", "Zuschauer" },
            new[] { "tut.1", "Fareyi oynat: renkli nokta senin elin. Parlayan bir kayanın üstüne getir ve SOL TIKLA!", "Move the mouse: the colored dot is your hand. Put it on a glowing rock and LEFT CLICK!", "Bewege die Maus: der farbige Punkt ist deine Hand. Auf einen leuchtenden Stein und LINKSKLICK!" },
            new[] { "tut.2", "Süper! Kontrol diğer eline geçti. Daha yukarıdaki bir kayaya uzan ve tıkla.", "Great! Control passed to your other hand. Reach a higher rock and click.", "Super! Jetzt steuerst du die andere Hand. Greif einen höheren Stein und klick." },
            new[] { "tut.3", "Harika! Tutunan el seni yukarı çeker. Böyle devam et, zirve seni bekliyor!", "Awesome! The holding hand pulls you up. Keep going, the summit awaits!", "Klasse! Die haltende Hand zieht dich hoch. Weiter so, der Gipfel wartet!" },
            new[] { "profile.default", "Dağcı", "Climber", "Kletterer" },
        };

        static readonly string[] Langs = { "Turkish", "English", "German" };

        [MenuItem("FumblePeak/Kurulum/Çevirileri Yaz (TR-EN-DE)", priority = 10)]
        public static void Seed()
        {
            var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(TablePath);
            if (table == null)
            {
                Debug.LogError("[FumblePeak] LocalizationTable bulunamadı: " + TablePath);
                return;
            }

            // Dil asset'lerini bul / oluştur
            var langAssets = new LocalizationLanguage[Langs.Length];
            for (int l = 0; l < Langs.Length; l++)
            {
                var asset = table.Languages.FirstOrDefault(x => x != null && x.LanguageName == Langs[l]);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<LocalizationLanguage>();
                    asset.LanguageName = Langs[l];
                    AssetDatabase.CreateAsset(asset, $"{LocDir}/(Language) {Langs[l]}.asset");
                    table.Languages.Add(asset);
                }
                langAssets[l] = asset;
            }

            int added = 0, updated = 0;
            foreach (var row in S)
            {
                string full = row[0];
                int dot = full.IndexOf('.');
                string sectionName = full.Substring(0, dot);
                string sub = full.Substring(dot + 1);

                var section = table.TableSheet.FirstOrDefault(x => x.SectionName == sectionName);
                if (section == null)
                {
                    section = new TableSheetSection(sectionName);
                    table.TableSheet.Add(section);
                }
                var item = section.SectionSheet.FirstOrDefault(x => x.Key == sub);
                if (item == null)
                {
                    item = new SheetItem(sub);
                    section.SectionSheet.Add(item);
                    added++;
                }

                for (int l = 0; l < Langs.Length; l++)
                {
                    var strings = langAssets[l].Strings;
                    var entry = strings.FirstOrDefault(x => x.Key == full);
                    if (entry == null)
                    {
                        entry = new LocalizationLanguage.LocalizationString();
                        strings.Add(entry);
                    }
                    else updated++;
                    entry.SectionId = section.Id;
                    entry.EntryId = item.Id;
                    entry.Key = full;
                    entry.Value = row[l + 1];
                }
            }

            // Türkçe varsayılan olsun (ilk dil = kayıt yoksa ilk açılış dili yedeği)
            var tr = table.Languages.FirstOrDefault(x => x != null && x.LanguageName == "Turkish");
            if (tr != null) { table.Languages.Remove(tr); table.Languages.Insert(0, tr); }

            EditorUtility.SetDirty(table);
            foreach (var a in langAssets) EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log($"<b>[FumblePeak]</b> Çeviriler yazıldı: {S.Length} anahtar ({added} yeni) × {Langs.Length} dil.");
        }
    }
}
