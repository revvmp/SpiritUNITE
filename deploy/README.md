# Hosting with nginx

The game only logs in over HTTPS with a real certificate. SpiritUNITE's web server speaks plain HTTP on `127.0.0.1:10081`, so nginx sits in front of it and handles HTTPS:

```
Player ──HTTPS :443──> nginx ──> SpiritUNITE 127.0.0.1:10081   (login pages, images)
Player ──HTTP  :80───> nginx ──> SpiritUNITE 127.0.0.1:10081   (game resource download)
```

Replace `play.example.com` with your domain everywhere.

## 1. Open Your Ports

Open these ports:

| Port | Used for |
| :--- | :--- |
| TCP 80 | Resource download, certificate checks |
| TCP 443 | Login pages and images |
| TCP 8085 | Update server |
| TCP 10001 | Directory server |
| TCP 10002 | Lobby server |
| UDP 10003 | Battles |
| UDP 8700 | Voice chat |

With `ufw`:

```bash
sudo ufw allow 80,443,8085,10001,10002/tcp
sudo ufw allow 10003,8700/udp
```


## 2. Install nginx and certbot

```bash
sudo apt update
sudo apt install nginx certbot python3-certbot-nginx
```

## 3. Get a Certificate

Certbot gets a free certificate from Let's Encrypt and renews it automatically.

```bash
sudo certbot certonly --nginx -d play.example.com --deploy-hook "systemctl reload nginx"
```

Any publicly trusted certificate works instead. Point the two `ssl_certificate` lines in the config at it.

## 4. Add the nginx Config

From the SpiritUNITE folder:

```bash
sudo cp deploy/nginx-spiritunite.conf /etc/nginx/sites-available/spiritunite
sudo sed -i 's/play.example.com/YOUR.DOMAIN/g' /etc/nginx/sites-available/spiritunite
sudo ln -s /etc/nginx/sites-available/spiritunite /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
```

## 5. Show Real Player Addresses (Optional)

Without this, SpiritUNITE's logs show every web request as coming from `127.0.0.1`. In `appsettings.json`:

```json
"HttpServer": {
  "BindAddress": "127.0.0.1",
  "Port": 10081,
  "TrustedProxies": ["127.0.0.1"]
}
```

Restart SpiritUNITE after changing it.
