namespace Ferrite.Core.Tests.Infrastructure;

/// <summary>
/// Version-document fixtures mirroring the real metadata shapes: a modern document with
/// rule-filtered arguments and <c>default-user-jvm</c>, and a legacy document with
/// <c>minecraftArguments</c> and a natives classifier map.
/// </summary>
internal static class VersionFixtures
{
    public const string ModernRelease = """
    {
      "id": "26.3",
      "type": "release",
      "mainClass": "net.minecraft.client.main.Main",
      "assets": "34",
      "assetIndex": { "id": "34", "sha1": "assetindexsha1", "size": 10, "totalSize": 20, "url": "https://example.invalid/34.json" },
      "downloads": { "client": { "sha1": "clientsha1", "size": 100, "url": "https://example.invalid/client.jar" } },
      "javaVersion": { "component": "java-runtime-epsilon", "majorVersion": 25 },
      "logging": {
        "client": {
          "argument": "-Dlog4j.configurationFile=${path}",
          "file": { "id": "client-1.21.2.xml", "sha1": "logsha1", "size": 10, "url": "https://example.invalid/log.xml" },
          "type": "log4j2-xml"
        }
      },
      "libraries": [
        {
          "name": "com.example:demo:1.0",
          "downloads": { "artifact": { "path": "com/example/demo/1.0/demo-1.0.jar", "sha1": "aa", "size": 10, "url": "https://example.invalid/demo.jar" } }
        },
        {
          "name": "org.example:native:1.0:natives-windows",
          "downloads": { "artifact": { "path": "org/example/native/1.0/native-1.0-natives-windows.jar", "sha1": "bb", "size": 10, "url": "https://example.invalid/native.jar" } },
          "rules": [ { "action": "allow", "os": { "name": "windows" } } ]
        }
      ],
      "arguments": {
        "default-user-jvm": [
          { "value": ["-Xms2G", "-Xmx4G", "-XX:+UseStringDeduplication"] },
          {
            "rules": [ { "action": "allow", "os": { "name": "windows", "versionRange": { "min": "10.0.17134" } } } ],
            "value": ["-XX:+UseZGC"]
          }
        ],
        "jvm": [
          { "rules": [ { "action": "allow", "os": { "name": "windows" } } ], "value": "-Djava.library.path=${natives_directory}/java" },
          "-cp",
          "${classpath}"
        ],
        "game": [
          "--username", "${auth_player_name}",
          "--version", "${version_name}",
          "--gameDir", "${game_directory}",
          "--assetsDir", "${assets_root}",
          "--assetIndex", "${assets_index_name}",
          "--uuid", "${auth_uuid}",
          "--accessToken", "${auth_access_token}",
          "--clientId", "${clientid}",
          "--xuid", "${auth_xuid}",
          "--versionType", "${version_type}",
          { "rules": [ { "action": "allow", "features": { "is_demo_user": true } } ], "value": "--demo" },
          { "rules": [ { "action": "allow", "features": { "has_custom_resolution": true } } ], "value": "--width ${resolution_width} --height ${resolution_height}" },
          { "rules": [ { "action": "allow", "features": { "has_quick_plays_support": true } } ], "value": "--quickPlayPath ${quickPlayPath}" },
          { "rules": [ { "action": "allow", "features": { "is_quick_play_multiplayer": true } } ], "value": "--quickPlayMultiplayer ${quickPlayMultiplayer}" }
        ]
      }
    }
    """;

    public const string LegacyRelease = """
    {
      "id": "1.12.2",
      "type": "release",
      "mainClass": "net.minecraft.client.main.Main",
      "assets": "1.12",
      "assetIndex": { "id": "1.12", "sha1": "legacyassets", "size": 10, "totalSize": 20, "url": "https://example.invalid/1.12.json" },
      "downloads": { "client": { "sha1": "legacyclient", "size": 100, "url": "https://example.invalid/1.12.2.jar" } },
      "minecraftArguments": "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_session} --userType ${user_type} --versionType ${version_type}",
      "libraries": [
        {
          "name": "org.lwjgl.lwjgl:lwjgl:2.9.4",
          "downloads": {
            "artifact": { "path": "org/lwjgl/lwjgl/lwjgl/2.9.4/lwjgl-2.9.4.jar", "sha1": "cc", "size": 10, "url": "https://example.invalid/lwjgl.jar" },
            "classifiers": {
              "natives-windows": { "path": "org/lwjgl/lwjgl/lwjgl/2.9.4/lwjgl-2.9.4-natives-windows.jar", "sha1": "dd", "size": 10, "url": "https://example.invalid/lwjgl-natives.jar" }
            }
          },
          "natives": { "windows": "natives-windows", "linux": "natives-linux", "osx": "natives-osx" },
          "extract": { "exclude": ["META-INF/"] }
        }
      ]
    }
    """;
}
