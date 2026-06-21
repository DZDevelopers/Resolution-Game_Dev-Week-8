using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

public class Program
{
    const int Width = 960;
    const int Height = 540;

    const float PlayerRadius = 18f;
    const float EnemyRadius = 14f;
    const float ShotRadius = 10f;

    const float PlayerSpeed = 300f;
    const float ShotSpeed = 420f;
    const float ShotTelegraphTime = 0.5f;
    const float ShotSpawnInterval = 3.5f;

    static Random rng = new Random();

    static Vector2 player;
    static List<Vector2> enemies = new();
    static List<Vector2> velocities = new();

    class Shot
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float TelegraphTimer;
        public bool Fired;
    }
    static List<Shot> shots = new();
    static float shotSpawnTimer;

    enum GameState { Menu, Playing, Paused, GameOver, Upgrade }
    static GameState state = GameState.Menu;

    static bool gameOver = false;
    static float timeAlive = 0;
    static float highScore = 0;

    static float shakeTimer = 0;
    static float shakeStrength = 0;

    enum UpgradeType { ExtraLife, SpeedBoost, TimeSlow, SmallerHitbox, Shield }

    class UpgradeOption
    {
        public UpgradeType Type;
        public string Name;
        public string Description;
    }

    const float UpgradeInterval = 30f;
    static float nextUpgradeAt = UpgradeInterval;

    static Dictionary<UpgradeType, int> upgradeStacks = new()
    {
        { UpgradeType.ExtraLife, 0 },
        { UpgradeType.SpeedBoost, 0 },
        { UpgradeType.TimeSlow, 0 },
        { UpgradeType.SmallerHitbox, 0 },
        { UpgradeType.Shield, 0 },
    };

    static int extraLives = 0;
    static int selectedUpgradeIndex = 0;
    static List<UpgradeOption> currentChoices = new();

    static float invincibleTimer = 0;

    const float SpeedBoostPerStack = 35f;
    const float TimeSlowPerStack = 0.10f;
    const float HitboxReductionPerStack = 1.5f;
    const float MinPlayerRadius = 8f;
    const float ShieldSecondsPerStack = 1.0f;

    static float CurrentPlayerSpeed =>
        PlayerSpeed + upgradeStacks[UpgradeType.SpeedBoost] * SpeedBoostPerStack;

    static float CurrentPlayerRadius =>
        MathF.Max(MinPlayerRadius, PlayerRadius - upgradeStacks[UpgradeType.SmallerHitbox] * HitboxReductionPerStack);

    static float CurrentEnemySpeedMul
    {
        get
        {
            float slow = upgradeStacks[UpgradeType.TimeSlow] * TimeSlowPerStack;
            return MathF.Max(0.25f, 1f - slow);
        }
    }

    public static void Main()
    {
        Raylib.InitWindow(Width, Height, "Dodge Ball Game");
        Raylib.SetTargetFPS(60);

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            switch (state)
            {
                case GameState.Menu:
                    UpdateMenu();
                    break;

                case GameState.Playing:
                    UpdatePlayer(dt);
                    UpdateEnemies(dt);
                    UpdateShots(dt);
                    CheckCollision();
                    timeAlive += dt;

                    if (invincibleTimer > 0)
                        invincibleTimer -= dt;

                    if (timeAlive >= nextUpgradeAt)
                        OpenUpgradeChoice();

                    if (Raylib.IsKeyPressed(KeyboardKey.P) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                        state = GameState.Paused;
                    break;

                case GameState.Paused:
                    if (Raylib.IsKeyPressed(KeyboardKey.P) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                        state = GameState.Playing;
                    break;

                case GameState.Upgrade:
                    UpdateUpgradeChoice();
                    break;

                case GameState.GameOver:
                    if (Raylib.IsKeyPressed(KeyboardKey.R))
                        Reset();
                    if (Raylib.IsKeyPressed(KeyboardKey.M))
                        state = GameState.Menu;
                    break;
            }

            if (shakeTimer > 0)
                shakeTimer -= dt;

            Draw();
        }

        Raylib.CloseWindow();
    }

    static void UpdateMenu()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
            Reset();
        if (Raylib.IsKeyPressed(KeyboardKey.Q))
            Raylib.CloseWindow();
    }

    static void Reset()
    {
        player = new Vector2(Width / 2, Height / 2);
        enemies.Clear();
        velocities.Clear();
        shots.Clear();
        shotSpawnTimer = ShotSpawnInterval;
        gameOver = false;
        timeAlive = 0;
        shakeTimer = 0;

        nextUpgradeAt = UpgradeInterval;
        extraLives = 0;
        invincibleTimer = 0;
        foreach (var key in new List<UpgradeType>(upgradeStacks.Keys))
            upgradeStacks[key] = 0;

        for (int i = 0; i < 8; i++)
        {
            enemies.Add(RandomEdge());
            velocities.Add(RandomVelocity(140f));
        }

        state = GameState.Playing;
    }

    static void UpdatePlayer(float dt)
    {
        Vector2 dir = Vector2.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.W)) dir.Y -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) dir.Y += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A)) dir.X -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D)) dir.X += 1;

        if (dir != Vector2.Zero)
            dir = Vector2.Normalize(dir);

        player += dir * CurrentPlayerSpeed * dt;

        float r = CurrentPlayerRadius;
        player.X = Clamp(player.X, r, Width - r);
        player.Y = Clamp(player.Y, r, Height - r);
    }

    static void UpdateEnemies(float dt)
    {
        float rampMul = 1f + MathF.Min(timeAlive / 30f, 1.2f);
        float speedMul = rampMul * CurrentEnemySpeedMul;

        for (int i = 0; i < enemies.Count; i++)
        {
            Vector2 pos = enemies[i];
            Vector2 vel = velocities[i];

            pos += vel * speedMul * dt;

            if (pos.X < 0 || pos.X > Width) vel.X *= -1;
            if (pos.Y < 0 || pos.Y > Height) vel.Y *= -1;

            enemies[i] = pos;
            velocities[i] = vel;
        }
    }

    static void UpdateShots(float dt)
    {
        shotSpawnTimer -= dt;
        float interval = MathF.Max(1.5f, ShotSpawnInterval - timeAlive * 0.03f);
        if (shotSpawnTimer <= 0)
        {
            shotSpawnTimer = interval;
            shots.Add(new Shot
            {
                Pos = RandomEdge(),
                TelegraphTimer = ShotTelegraphTime,
                Fired = false
            });
        }

        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var s = shots[i];

            if (!s.Fired)
            {
                s.TelegraphTimer -= dt;
                if (s.TelegraphTimer <= 0)
                {
                    Vector2 toPlayer = player - s.Pos;
                    toPlayer = toPlayer != Vector2.Zero ? Vector2.Normalize(toPlayer) : new Vector2(1, 0);

                    s.Vel = toPlayer * ShotSpeed * CurrentEnemySpeedMul;
                    s.Fired = true;
                }
            }
            else
            {
                s.Pos += s.Vel * dt;

                if (s.Pos.X < -50 || s.Pos.X > Width + 50 || s.Pos.Y < -50 || s.Pos.Y > Height + 50)
                {
                    shots.RemoveAt(i);
                    continue;
                }
            }

            shots[i] = s;
        }
    }

    static readonly Dictionary<UpgradeType, (string Name, string Desc)> UpgradeInfo = new()
    {
        { UpgradeType.ExtraLife,     ("Extra Life",     "Survive one extra hit") },
        { UpgradeType.SpeedBoost,    ("Speed Boost",    "Move faster") },
        { UpgradeType.TimeSlow,      ("Time Slow",      "Enemies & shots move slower") },
        { UpgradeType.SmallerHitbox, ("Smaller Hitbox", "You're harder to hit") },
        { UpgradeType.Shield,        ("Shield",         "Brief invincibility now, and on future level-ups") },
    };

    static void OpenUpgradeChoice()
    {
        nextUpgradeAt += UpgradeInterval;

        if (upgradeStacks[UpgradeType.Shield] > 0)
            invincibleTimer = MathF.Max(invincibleTimer, upgradeStacks[UpgradeType.Shield] * ShieldSecondsPerStack);

        var types = new List<UpgradeType>((UpgradeType[])Enum.GetValues(typeof(UpgradeType)));
        currentChoices.Clear();

        for (int i = 0; i < 3 && types.Count > 0; i++)
        {
            int idx = rng.Next(types.Count);
            var t = types[idx];
            types.RemoveAt(idx);

            currentChoices.Add(new UpgradeOption
            {
                Type = t,
                Name = UpgradeInfo[t].Name,
                Description = UpgradeInfo[t].Desc
            });
        }

        selectedUpgradeIndex = 0;
        state = GameState.Upgrade;
    }

    static void UpdateUpgradeChoice()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.A) || Raylib.IsKeyPressed(KeyboardKey.Left))
            selectedUpgradeIndex = (selectedUpgradeIndex - 1 + currentChoices.Count) % currentChoices.Count;
        if (Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.Right))
            selectedUpgradeIndex = (selectedUpgradeIndex + 1) % currentChoices.Count;

        if (Raylib.IsKeyPressed(KeyboardKey.One)) selectedUpgradeIndex = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Two) && currentChoices.Count > 1) selectedUpgradeIndex = 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Three) && currentChoices.Count > 2) selectedUpgradeIndex = 2;

        bool numberPicked = Raylib.IsKeyPressed(KeyboardKey.One) ||
            (Raylib.IsKeyPressed(KeyboardKey.Two) && currentChoices.Count > 1) ||
            (Raylib.IsKeyPressed(KeyboardKey.Three) && currentChoices.Count > 2);

        if (numberPicked || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            ApplyUpgrade(currentChoices[selectedUpgradeIndex].Type);
            state = GameState.Playing;
        }
    }

    static void ApplyUpgrade(UpgradeType type)
    {
        upgradeStacks[type]++;

        if (type == UpgradeType.ExtraLife)
            extraLives++;
    }

    static void CheckCollision()
    {
        if (invincibleTimer > 0)
            return;

        float r = CurrentPlayerRadius;

        foreach (var e in enemies)
        {
            if (Vector2.Distance(player, e) < r + EnemyRadius)
            {
                HandleHit();
                return;
            }
        }

        foreach (var s in shots)
        {
            if (s.Fired && Vector2.Distance(player, s.Pos) < r + ShotRadius)
            {
                HandleHit();
                return;
            }
        }
    }

    static void HandleHit()
    {
        if (extraLives > 0)
        {
            extraLives--;
            invincibleTimer = 1.5f;
            shakeTimer = 0.25f;
            shakeStrength = 5f;

            for (int i = shots.Count - 1; i >= 0; i--)
            {
                if (Vector2.Distance(player, shots[i].Pos) < 80f)
                    shots.RemoveAt(i);
            }
        }
        else
        {
            TriggerGameOver();
        }
    }

    static void TriggerGameOver()
    {
        gameOver = true;
        state = GameState.GameOver;
        shakeTimer = 0.3f;
        shakeStrength = 6f;

        if (timeAlive > highScore)
            highScore = timeAlive;
    }

    static void Draw()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(20, 20, 30, 255));

        Vector2 shakeOffset = Vector2.Zero;
        if (shakeTimer > 0)
        {
            shakeOffset = new Vector2(
                (float)(rng.NextDouble() * 2 - 1) * shakeStrength,
                (float)(rng.NextDouble() * 2 - 1) * shakeStrength
            );
        }

        if (state == GameState.Menu)
            DrawMenu();
        else
            DrawGame(shakeOffset);

        Raylib.EndDrawing();
    }

    static void DrawMenu()
    {
        string title = "DODGE BALL";
        int titleSize = 50;
        int titleWidth = Raylib.MeasureText(title, titleSize);
        Raylib.DrawText(title, Width / 2 - titleWidth / 2, 140, titleSize, Color.White);

        DrawCentered("Press ENTER or SPACE to Play", 260, 22, Color.SkyBlue);
        DrawCentered("Press Q to Quit", 295, 20, Color.Gray);
        DrawCentered("WASD to move  |  Avoid red balls and blue shots", 380, 18, Color.LightGray);

        if (highScore > 0)
            DrawCentered($"Best Time: {highScore:0.0}s", 420, 20, Color.Gold);

        Raylib.DrawCircle(Width / 2, 470, ShotRadius, Color.SkyBlue);
        DrawCentered("Telegraphs briefly, then flies straight - dodge it!", 495, 16, Color.LightGray);
    }

    static void DrawGame(Vector2 shakeOffset)
    {
        Vector2 Off(Vector2 v) => v + shakeOffset;

        float playerR = CurrentPlayerRadius;
        if (invincibleTimer > 0)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 15f);
            Color glow = new Color(255, 230, 80, (int)(90 + pulse * 100));
            Raylib.DrawCircleV(Off(player), playerR + 8, glow);
        }
        Raylib.DrawCircleV(Off(player), playerR, Color.Blue);

        foreach (var e in enemies)
            Raylib.DrawCircleV(Off(e), EnemyRadius, Color.Red);

        foreach (var s in shots)
        {
            if (!s.Fired)
            {
                float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 20f);
                Color c = new Color((int)Color.SkyBlue.R, (int)Color.SkyBlue.G, (int)Color.SkyBlue.B, (int)(120 + pulse * 135));
                Raylib.DrawCircleV(Off(s.Pos), ShotRadius + 4, c);
                Raylib.DrawCircleLines((int)s.Pos.X, (int)s.Pos.Y, ShotRadius + 10, Color.SkyBlue);
            }
            else
            {
                Raylib.DrawCircleV(Off(s.Pos), ShotRadius, Color.SkyBlue);
            }
        }

        Raylib.DrawText($"Time: {timeAlive:0.0}", 20, 20, 20, Color.White);
        if (highScore > 0)
            Raylib.DrawText($"Best: {highScore:0.0}", 20, 45, 18, Color.Gold);

        DrawUpgradeHud();

        if (state == GameState.Paused)
        {
            Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 150));
            DrawCentered("PAUSED", 230, 40, Color.White);
            DrawCentered("Press P or ESC to resume", 280, 20, Color.LightGray);
        }

        if (state == GameState.Upgrade)
            DrawUpgradeChoice();

        if (state == GameState.GameOver)
        {
            Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 150));
            DrawCentered("GAME OVER", 230, 40, Color.Red);
            DrawCentered($"Survived: {timeAlive:0.0}s", 275, 22, Color.White);
            DrawCentered("Press R to restart  |  M for menu", 310, 20, Color.LightGray);
        }
    }

    static void DrawUpgradeHud()
    {
        string livesText = extraLives > 0 ? $"Lives +{extraLives}" : "";
        int x = Width - 20;

        if (extraLives > 0)
        {
            int w = Raylib.MeasureText(livesText, 18);
            Raylib.DrawText(livesText, x - w, 20, 18, Color.Pink);
            x -= w + 16;
        }

        foreach (var kv in upgradeStacks)
        {
            if (kv.Value <= 0 || kv.Key == UpgradeType.ExtraLife) continue;
            string label = $"{ShortName(kv.Key)} x{kv.Value}";
            int w = Raylib.MeasureText(label, 16);
            x -= w + 14;
            Raylib.DrawText(label, x, 22, 16, Color.SkyBlue);
        }
    }

    static string ShortName(UpgradeType t) => t switch
    {
        UpgradeType.SpeedBoost => "Speed",
        UpgradeType.TimeSlow => "Slow",
        UpgradeType.SmallerHitbox => "Small",
        UpgradeType.Shield => "Shield",
        _ => t.ToString()
    };

    static void DrawUpgradeChoice()
    {
        Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 170));
        DrawCentered("LEVEL UP!", 70, 36, Color.Gold);
        DrawCentered("Choose an upgrade", 115, 20, Color.LightGray);

        int cardCount = currentChoices.Count;
        int cardW = 220;
        int cardH = 220;
        int gap = 30;
        int totalW = cardCount * cardW + (cardCount - 1) * gap;
        int startX = Width / 2 - totalW / 2;
        int cardY = 170;

        for (int i = 0; i < cardCount; i++)
        {
            int cx = startX + i * (cardW + gap);
            bool selected = i == selectedUpgradeIndex;

            var border = selected ? Color.Gold : Color.Gray;
            var fill = selected ? new Color(50, 50, 70, 255) : new Color(35, 35, 45, 255);

            Raylib.DrawRectangle(cx, cardY, cardW, cardH, fill);
            Raylib.DrawRectangleLinesEx(new Rectangle(cx, cardY, cardW, cardH), selected ? 4 : 2, border);

            var opt = currentChoices[i];
            int stacks = upgradeStacks[opt.Type];

            string number = $"{i + 1}";
            Raylib.DrawText(number, cx + 16, cardY + 12, 22, Color.LightGray);

            int nameW = Raylib.MeasureText(opt.Name, 20);
            Raylib.DrawText(opt.Name, cx + cardW / 2 - nameW / 2, cardY + 50, 20, Color.White);
            DrawWrapped(opt.Description, cx + 16, cardY + 90, cardW - 32, 16, Color.LightGray);

            if (stacks > 0)
            {
                string stackText = $"Owned: {stacks}";
                int sw = Raylib.MeasureText(stackText, 16);
                Raylib.DrawText(stackText, cx + cardW / 2 - sw / 2, cardY + cardH - 30, 16, Color.SkyBlue);
            }
        }

        DrawCentered("A/D or Arrows to pick, Enter/Space to confirm (or press 1/2/3)", Height - 50, 16, Color.LightGray);
    }

    static void DrawWrapped(string text, int x, int y, int maxWidth, int fontSize, Color color)
    {
        string[] words = text.Split(' ');
        string line = "";
        int lineY = y;

        foreach (var word in words)
        {
            string test = line.Length == 0 ? word : line + " " + word;
            if (Raylib.MeasureText(test, fontSize) > maxWidth)
            {
                Raylib.DrawText(line, x, lineY, fontSize, color);
                line = word;
                lineY += fontSize + 4;
            }
            else
            {
                line = test;
            }
        }

        if (line.Length > 0)
            Raylib.DrawText(line, x, lineY, fontSize, color);
    }

    static void DrawCentered(string text, int y, int size, Color color)
    {
        int w = Raylib.MeasureText(text, size);
        Raylib.DrawText(text, Width / 2 - w / 2, y, size, color);
    }

    static Vector2 RandomEdge()
    {
        int side = rng.Next(4);
        return side switch
        {
            0 => new Vector2(rng.Next(Width), 0),
            1 => new Vector2(rng.Next(Width), Height),
            2 => new Vector2(0, rng.Next(Height)),
            _ => new Vector2(Width, rng.Next(Height))
        };
    }

    static Vector2 RandomVelocity(float speed)
    {
        float angle = (float)(rng.NextDouble() * Math.PI * 2);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
    }

    static float Clamp(float v, float min, float max)
        => MathF.Max(min, MathF.Min(max, v));
}