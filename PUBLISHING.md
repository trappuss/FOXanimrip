# Publishing to GitHub — first time

Written for someone who has not used GitHub before. Follow it top to bottom;
it should take about ten minutes.

Your repository folder is:

```
C:\Users\notso\Downloads\Claude Current\FOXanimtool\foxanimrip-repo-1.23.0
```

The built `.exe` files are already staged in `dist\`, so you do **not** need the
.NET SDK, Python, or FoxBrowser reference assemblies. You need two programs.

---

## Step 1 — Install Git

Download and run: **https://git-scm.com/download/win**

Accept every default. This gives you the `git` command.

## Step 2 — Install the GitHub CLI

Download and run: **https://cli.github.com**

Accept every default. This gives you the `gh` command, which creates the
repository and the release for you.

**Then close any open terminal windows** — new programs only appear on the PATH
in terminals opened afterwards.

## Step 3 — Open a terminal in the repository folder

Open the folder in File Explorer, click the address bar, type `cmd`, press
Enter. A black window opens already in the right place.

Check both tools are visible:

```bat
git --version
gh --version
```

Both should print a version number. If either says "not recognized", close the
window, open a new one, and try again.

## Step 4 — Tell Git who you are

Only needed once, ever. Use any name; the email can be the one on your GitHub
account.

```bat
git config --global user.name "your name"
git config --global user.email "you@example.com"
```

## Step 5 — Sign in to GitHub

```bat
gh auth login
```

Answer the prompts:

| prompt | answer |
|--------|--------|
| What account do you want to log into? | **GitHub.com** |
| What is your preferred protocol? | **HTTPS** |
| Authenticate Git with your GitHub credentials? | **Yes** |
| How would you like to authenticate? | **Login with a web browser** |

It shows a one-time code, then opens your browser. Paste the code, approve, and
return to the terminal. It should end with "Logged in as …".

## Step 6 — Create the repository and upload the code

Run these one at a time, in the repository folder:

```bat
git init
git add -A
git commit -m "foxanimrip 1.23.0"
git branch -M main
gh repo create FOXanimrip --public --source . --remote origin --push
```

The last line creates `github.com/<you>/FOXanimrip` and uploads everything.

*Prefer it private?* Use `--private` instead of `--public`. The wiki still
works; only people you invite can see it.

Nothing here contains game assets, and `dist\` is excluded from the repository
on purpose — the `.exe` files are attached to the Release instead.

## Step 7 — Create the wiki's first page (GitHub requires this once)

GitHub does not create a wiki until a page exists, so the script cannot make
the very first one.

1. Go to `https://github.com/<your-username>/FOXanimrip/wiki`
2. Click **Create the first page**
3. Type anything at all — a single character is fine
4. Click **Save page**

The script overwrites it with the real handbook in the next step.

> If you do not see a Wiki tab: **Settings → General → Features → tick Wikis**.

## Step 8 — Publish

Still in the repository folder:

```bat
scripts\push-github.bat --dry-run --no-build
```

That changes nothing and shows what it would do. If it looks right, do it for
real:

```bat
scripts\push-github.bat --no-build
```

It will:

1. package the release from the staged `dist\` binaries
2. commit and push anything that changed
3. create the **v1.23.0** release and upload the three zips
4. copy all eight handbook pages into the wiki and push them

When it finishes it prints the two links.

---

## Afterwards

**Check it looks right:**

- `https://github.com/<you>/FOXanimrip` — the README, with the handbook links
- `https://github.com/<you>/FOXanimrip/releases` — v1.23.0 with three zips
- `https://github.com/<you>/FOXanimrip/wiki` — eight pages, sidebar on the right

**To change something later** — edit the files, then run
`scripts\push-github.bat --no-build` again. It commits, pushes, updates the
release, and re-syncs the wiki. To publish a new version, change `<Version>` in
`src\Directory.Build.props` first; everything else follows from it.

**To edit only the wiki:** `scripts\push-github.bat --wiki-only`

**If you ever want to rebuild the binaries from source**, you additionally need
the .NET 10 SDK and FoxBrowser's reference assemblies:

```bat
python tools\extract-refs.py "C:\Users\notso\Desktop\RIP\MGSV\FoxBrowser 2531 9 2026-08-07T00-34Z FL4wF6PaE\FoxBrowser.exe" refs
set FoxBrowserRefDir=%CD%\refs
scripts\push-github.bat
```

(without `--no-build`).

---

## If something goes wrong

| message | what to do |
|---------|-----------|
| `git` or `gh` "not recognized" | close the terminal, open a new one |
| `gh is not authenticated` | run `gh auth login` again (Step 5) |
| `could not clone the wiki` | Step 7 was skipped — create one wiki page, then `scripts\push-github.bat --wiki-only` |
| `run this from the repository root` | you are in the wrong folder; `cd` into `foxanimrip-repo-1.23.0` |
| `Authentication failed` on push | `gh auth login` again and choose **Yes** to "Authenticate Git with your GitHub credentials" |
| release already exists | that is fine — it replaces the files and notes |
