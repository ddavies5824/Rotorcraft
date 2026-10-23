# Setting up Google sign-in, cloud saves and the pilot area

Everything in the game works offline without this. Doing this setup adds:

- **Cloud saves.** Your career, freeplay record and company are kept in your own Google Drive, in a hidden app folder only the game can see. They follow you between the Windows game and the web version.
- **Pilot area.** Leaderboards and pilot profiles, stored in a free Firebase project.

It takes about 15–20 minutes, once. You need a Google account.

---

## Part 1: Google Cloud project and sign-in (cloud saves)

1. Go to **https://console.cloud.google.com/** and sign in.
2. Create a project. Use the project picker at the top, then **New project**. Name it `Rotorcraft` and click **Create**, then make sure it is selected.
3. Turn on the Drive API:
   1. Go to **APIs & Services › Library**.
   2. Search for **Google Drive API**, open it and click **Enable**.
4. Set up the consent screen (Google calls it "Google Auth Platform" or "OAuth consent screen"):
   1. Click **Get started**.
   2. App name: `Rotorcraft`. Support email: your email.
   3. Audience: **External**.
   4. Contact email: your email. Agree to the terms and click **Create**.
   5. Under **Data access** (Scopes), click **Add or remove scopes** and tick:
      - `.../auth/drive.appdata` (see, create and delete its own configuration data in your Google Drive)
      - `openid`
      - `.../auth/userinfo.email`

      Then click **Update** and **Save**.
   6. Under **Audience** › **Test users**, add your own Gmail address. Add anyone else who will play too.

      While the app is in "Testing", only test users can sign in, and Google asks testers to sign in again about once a week. That is fine for personal use. To remove both limits, click **Publish app**. Google may ask for verification because of the Drive scope, but `drive.appdata` is a non-sensitive scope, so it is usually quick.
5. Create a client for the **Windows game**:
   1. Go to **Clients** (or **Credentials**) › **Create client**.
   2. Application type: **Desktop app**. Name: `Rotorcraft Windows`. Click **Create**.
   3. Click **Download JSON**. Keep this file. It holds the client id and client secret. A desktop app's "secret" is not really secret; Google expects it to ship inside the app.
6. Create a client for the **web version** (Netlify):
   1. **Create client** › Application type: **Web application**. Name: `Rotorcraft Web`.
   2. Under **Authorised JavaScript origins**, add your Netlify address, for example `https://your-site-name.netlify.app`. Use no trailing slash and no path. If you also test locally, add `http://localhost:8000` as well.
   3. Leave **Authorised redirect URIs** empty and click **Create**.
   4. Copy the **Client ID**. The web version needs only the id, never the secret.

## Part 2: Firebase (pilot area and leaderboards). Optional.

1. Go to **https://console.firebase.google.com/** and click **Add project**.
2. Pick the **same Google Cloud project** (`Rotorcraft`) from the list so everything stays together. Google Analytics is not needed.
3. **Authentication**:
   1. Go to **Build › Authentication › Get started**.
   2. Under **Sign-in method**, enable **Google** and save.
   3. Under **Settings › Authorised domains**, add your Netlify domain (`your-site-name.netlify.app`). `localhost` is already there.
4. **Firestore**:
   1. Go to **Build › Firestore Database › Create database**.
   2. Choose a location near you (for example `europe-west2`, London) and **production mode**.
5. Open the Firestore **Rules** tab, replace everything with the rules below, and click **Publish**:

   ```
   rules_version = '2';
   service cloud.firestore {
     match /databases/{db}/documents {
       match /pilots/{uid} {
         allow read: if true;
         allow write: if request.auth != null && request.auth.uid == uid
                      && request.resource.data.keys().hasOnly(['name','rank','rankLevel','rankScore','hours','flights','landings','smoothPct','accidents','rescues','passengers','operational','quals','qualCount','bestStanding','careerEarned','fpShifts','fpJobs','fpEarned','fpBest','acHours','startedAt','updatedAt'])
                      && request.resource.data.name is string && request.resource.data.name.size() <= 24;
         allow delete: if request.auth != null && request.auth.uid == uid;
       }
     }
   }
   ```

   This means anyone can read the leaderboards, but each pilot can only write their own profile, with game statistics only.
6. Get the web config: **Project settings** (gear icon) › **General** › **Your apps**.
   1. Click the **web** icon `</>` and register an app called `Rotorcraft`. Hosting is not needed.
   2. Copy the `firebaseConfig` block. Only `apiKey` and `projectId` are used.

## Part 3: Put the values in the game

There are two ways. Pick one.

**A. Once, for every copy you build (recommended).** Edit
`Assets/Rotorcraft/Resources/OnlineConfig.json` in the Unity project, then rebuild:

```json
{
  "googleClientId": "1234-abc.apps.googleusercontent.com",      (Desktop app client id)
  "googleClientSecret": "GOCSPX-…",                              (Desktop app client secret)
  "googleWebClientId": "1234-xyz.apps.googleusercontent.com",   (Web application client id)
  "firebaseApiKey": "AIza…",
  "firebaseProjectId": "rotorcraft-12345"
}
```

(Don't keep the notes in brackets; JSON doesn't allow comments.)

**B. In the game, without rebuilding.** Open **Login** from the main menu and scroll to **Set up online play**:

- **Google client:** in Windows, paste the whole downloaded Desktop-app JSON file. On the web, paste the Web client id.
- **Firebase:** paste the `firebaseConfig` block as Firebase shows it.

Values pasted this way are saved on that computer or browser only.

## Part 4: Check it works

1. **Windows.** Main menu › **Login** › **Sign in with Google**.
   1. Your browser opens Google's page. Pick your account and allow access.
   2. The tab says "Signed in to Rotorcraft". Go back to the game.
   3. The game shows your email and "Saved to Google Drive".
   4. If Windows Firewall asks about Rotorcraft, allow **private networks**. The game listens on 127.0.0.1 only, for Google's answer.
2. **Web.** On your Netlify site, open **Login** › **Sign in with Google**.
   1. When the game says "Click anywhere on the page…", click once. Browsers only allow the Google popup after a real click.
   2. Choose your account. Your Windows career should load.
   3. On the web, Google sessions last about an hour, then you sign in again with one click. Your progress is also kept in the browser in the meantime.
3. **Pilot area.** After signing in, fly any exercise, then open **Pilots** from the main menu. Your profile should be on the boards.

### If something goes wrong

| Message | Fix |
|---|---|
| `Error 400: redirect_uri_mismatch` (web) | The Netlify address is missing from **Authorised JavaScript origins**, or has a typo or a trailing slash. Changes can take a few minutes to apply. |
| `access_denied` / "app has not completed verification" | Add your Gmail as a **Test user** (Part 1, step 4.6), or publish the app. |
| "Sign-in failed (401) invalid_client" (Windows) | Paste the **Desktop app** JSON, not the web client. |
| "Uploading the save failed (403)" | The **Google Drive API** is not enabled (Part 1, step 3). |
| Pilot area: "PERMISSION_DENIED" | The Firestore rules were not published, or Google is not enabled in Firebase Authentication. |
| Pilot area: "unauthorized domain" (web) | Add the Netlify domain under Firebase **Authentication › Settings › Authorised domains**. |
| Nothing happens on the web after clicking | A popup blocker stopped it: allow popups for your Netlify site. |

---

## Uploading the web version to Netlify

1. Log in at **https://app.netlify.com/**.
2. Go to **Sites** › **Add new site** › **Deploy manually**.
3. Drag the zip `Rotorcraft-Web.zip` (in `F:\Rotorcraft\Builds`) onto the page. Netlify accepts the zip as it is. If it doesn't, unzip it and drag the folder that holds `index.html`.
4. Netlify gives you an address like `https://random-name-123.netlify.app`. To change it, go to **Site configuration › Change site name**. Put that address in the Google web client and in Firebase (Parts 1.6 and 2.3).
5. To update later, open the site › **Deploys** and drag the new zip onto the page.

No server settings are needed: the game files decompress themselves in the browser, so any static host works.

The game needs a desktop browser with WebGL 2: Chrome, Edge or Firefox. Phones and tablets are not supported, and a mouse or a joystick is recommended. The first start downloads about 70 MB; later visits load from the browser cache. The first flight builds the world in the browser, which takes longer than on Windows, so the loading screen can sit for a minute or two. Progress is saved in the browser (and in Google Drive when signed in).
