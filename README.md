# SpiritUNITE

SpiritUNITE is a C# server emulator for the [Pokemon UNITE](https://en.wikipedia.org/wiki/Pok%C3%A9mon_Unite) client. The official service ends on **March 23, 2027**, and this project aims to keep the game playable after that.

## Gallery

Screenshots coming soon.

## Environment Setup

**WARNING**: These instructions are meant for a Windows 11 machine and have only been tested on Windows.

1. Clone the repository
2. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
3. Install [MariaDB](https://mariadb.org/download/)
4. Install [Python 3](https://www.python.org/downloads/)
   - Make sure to tick `Add to PATH` while installing. You can check by running `python --version` in your terminal.

## Server Setup

1. Generate the protocol files from your own copy of the client. Nothing taken from the client is included in this repository.
   ```powershell
   python scripts/generate_protos.py path/to/dump.cs
   ```

2. Create the database and its tables.
   ```powershell
   mariadb -u root -p < sql/spiritunite.sql
   ```
   The script is safe to run again. Rerun it after pulling new changes to bring your database up to date.

3. Create a database user for the server.
   ```sql
   CREATE USER 'spiritunite'@'localhost' IDENTIFIED BY 'your-password';
   GRANT ALL PRIVILEGES ON spiritunite.* TO 'spiritunite'@'localhost';
   ```

4. Copy the example settings and put your database password in the connection string.
   ```powershell
   copy src/SpiritUNITE/appsettings.example.json src/SpiritUNITE/appsettings.json
   ```
   **DO NOT** commit `appsettings.json`. It holds your password.

5. Build and start the server from the root directory.
   ```powershell
   dotnet run --project src/SpiritUNITE
   ```

6. Point your client at the server and launch the game. You should see logs in the server as you log in.

## Ports

| Port | Server |
| :--- | :--- |
| 10001 | Directory server |
| 10002 | Lobby server |
| 10080 | Account login and registration pages |

All of these can be changed in `appsettings.json`.

### Hosting for Others (Remote Play)

The steps above are all you need when the client and server run on the same machine. For friends to connect over a LAN or the internet, there are a few extra steps.

The directory server tells the client where to find the lobby server. By default it says `127.0.0.1`, which only works on your own machine. A remote client sent to `127.0.0.1` will never reach the lobby.

1. In `appsettings.json`, set `Lobby.Host` to your public IP or domain.
   ```json
   "Lobby": {
     "Host": "your.public.ip.or.domain",
     "Port": 10002
   }
   ```
2. Set `TcpServer.BindAddress` and `HttpServer.BindAddress` to `0.0.0.0` so the server accepts outside connections.
3. Forward or open ports `10001`, `10002` and `10080` on your router and firewall.
