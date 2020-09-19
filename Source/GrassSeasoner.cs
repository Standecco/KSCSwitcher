using System;
using UniLinq;
using UnityEngine;

namespace regexKSP
{
    // FIXME: colors are pretty bad
    public static class GrassColors
    {
        public static Color KSCDefault => new Color(0.28f, 0.319f, 0.2f);
        public static Color Dark => new Color(0.117f, 0.231f, 0.02f);
        public static Color Dry => new Color(0.663f, 0.659f, 0.008f);
        public static Color Unhealthy => new Color(0.589f, 0.52f, 0.031f);
        public static Color Healthy => new Color(0.314f, 0.463f, 0.02f);
        public static Color Snow => new Color(0.93f, 0.93f, 0.9f);
        public static Color DryHSV => new ColorHSV(0.166f, 0.80f, 0.5f).ToColor();
        public static Color DarkHSV => new ColorHSV(0.333f, 0.8f, 0.4f).ToColor();
        public static Color UnhealthyHSV => new ColorHSV(0.2f, 0.8f, 0.5f).ToColor();
        public static Color HealthyHSV => new ColorHSV(0.333f, 0.8f, 0.5f).ToColor();
        public static Color SnowHSV => new ColorHSV(0f, 0f, 0.9f).ToColor();
    }

    // disable the addon for the moment
    //[KSPAddon(KSPAddon.Startup.FlightAndKSC, false)]
    public class GrassSeasoner : MonoBehaviour
    {
        public static Material[] KSCGrassMaterials { get; private set; } = null;
        public static GrassSeasoner Instance;
        public static Color GroundColor
        {
            get
            {
                if (Instance?.originalGrassColor is Color col)
                {
                    return col;
                }
                else if (TryParseGroundColor(KSCSwitcher.KSCBody, KSCSwitcher.FindKSC().lat, KSCSwitcher.FindKSC().lon, out col))
                {
                    return col;
                }

                return new Color();
            }
        }

        public double lastUpdate = 0d;
        public double UpdateInterval { get => KSPUtil.dateTimeFormatter.Year / 300; }

        private Color originalGrassColor;
        private Color previousGrassColor;
        private bool firstUpdate = true;
        private PQSCity ksc = null;

        public void Start()
        {
            if (Instance != null)
                return;

            Instance = this;
            KSCGrassMaterials = Resources.FindObjectsOfTypeAll<Material>().Where(m => m.shader.name.Contains("KSC")).ToArray();
        }

        public void OnDestroy()
        {
            KSCGrassMaterials = null;
            Instance = null;
        }

        public void Update()
        {
            if (SpaceCenter.Instance == null)
                return;

            double time = Planetarium.GetUniversalTime();
            if (firstUpdate)
            {
                ksc = KSCSwitcher.FindKSC();
                TryParseGroundColor(KSCSwitcher.KSCBody, ksc.lat, ksc.lon, out originalGrassColor);
                // guarantee an update
                lastUpdate = time - UpdateInterval - 1;
                firstUpdate = false;
                previousGrassColor = originalGrassColor;
            }

            if (time < lastUpdate + UpdateInterval)
                return;

            lastUpdate = time;

            float lat = (float)ksc.lat;
            float alt = (float)ksc.alt;

            Color newColor = Color.Lerp(previousGrassColor, CalculateGrassColor(lat, alt, time), 0.75f);

            SetGrassColor(BlendedOriginal(newColor));
            Debug.Log("KSCSwitcher set grass color to: " + BlendedOriginal(newColor));

            previousGrassColor = newColor;
        }

        // FIXME
        private Color CalculateGrassColor(float lat, float alt, double time)
        {
            // data from http://www-das.uwyo.edu/~geerts/cwx/notes/chap16/geo_clim.html#:~:text=Data%20from%20a%20large%20number,the%20south%20(Fig%202).
            // and https://en.wikipedia.org/wiki/Position_of_the_Sun#Declination_of_the_Sun_as_seen_from_Earth

            bool southEmisphere = lat < 0;

            float avgTemperature = (-20 < lat && lat < 16) ? 27f : (southEmisphere ? 27 + 0.63f * (lat + 20) : 27 - 0.85f * (lat - 16));
            avgTemperature -= alt * 0.004f;
            float tempRange = 0.4f * lat;

            int noDays = (int)time % KSPUtil.dateTimeFormatter.Year / KSPUtil.dateTimeFormatter.Day;
            float sunDeclination = -23.44f * Mathf.Cos(0.0172142f * (noDays + 10));

            float seasonFactor = (southEmisphere ? -sunDeclination : sunDeclination) / 23.45f;

            float temp = avgTemperature + seasonFactor * tempRange;

            Debug.Log("KSCSwitcher calculated temp: " + temp);

            Color grass;

            if (temp < 0f)
            {
                // yellow at 0°C, completely white below -10°C
                grass = Color.Lerp(GrassColors.DarkHSV, GrassColors.SnowHSV, -temp / 10f);
            }
            else if (temp < 16f)
            {
                // yellow at 0°C, completely green above 16°C
                grass = Color.Lerp(GrassColors.DarkHSV, GrassColors.HealthyHSV, temp / 16f);
            }
            else if (temp < 26f)
            {
                // green at 15°C, yellow above 24°C
                grass = Color.Lerp(GrassColors.HealthyHSV, GrassColors.UnhealthyHSV, (temp - 16f) / 26f);
            }
            else
            {
                grass = Color.LerpUnclamped(GrassColors.UnhealthyHSV, GrassColors.DryHSV, (temp - 26f) / 40f);
            }

            if (seasonFactor >= 0)
            {
                grass = Color.Lerp(originalGrassColor, GrassColors.UnhealthyHSV, seasonFactor);
            }
            else
            {
                grass = Color.Lerp(originalGrassColor, GrassColors.SnowHSV, -seasonFactor);
            }

            return grass;
        }

        public static void SetGrassColor(Color newColor)
        {
            KSCGrassMaterials ??= Resources.FindObjectsOfTypeAll<Material>().Where(m => m.shader.name.Contains("KSC")).ToArray();

            for (int i = KSCGrassMaterials.Length; i-- > 0;)
            {
                KSCGrassMaterials[i].SetColor("_GrassColor", newColor);
            }
        }

        public static bool TryGetKSCGrassColor(CelestialBody home, ConfigNode pqsCity, out Color col)
        {
            col = new Color();
            if (pqsCity.HasValue("changeGrassColor"))
            {
                if (bool.TryParse(pqsCity.GetValue("changeGrassColor"), out bool btmp) && btmp)
                {
                    if (pqsCity.HasValue("grassColor"))
                    {
                        if (pqsCity.TryGetValue("grassColor", ref col))
                        {
                            Debug.Log($"KSCSwitcher found KSC grass color {col} from config");
                            return true;
                        }
                    }
                    else if (double.TryParse(pqsCity.GetValue("latitude"), out double lat) && double.TryParse(pqsCity.GetValue("longitude"), out double lon))
                    {
                        if(TryParseGroundColor(home, lat, lon, out col, 2f))
                            return true;
                    }
                }
            }
            return false;
        }

        public static bool TryParseGroundColor(CelestialBody body, double lat, double lon, out Color col, float colorMult = 1f)
        {
            col = new Color();
            // GetPixelColor(int x, int y) returns the color of the pixel of coordinates (x,y),
            // where (0,0) identifies the bottom right corner and (width, height) matches the top left corner;
            // KSP maps are both horizontally and vertically flipped, and longitude has a 1/4 width offset;
            // maps are flipped vertically again when stored in MAPSO;
            // therefore:
            // latitude = +90 =>    y = height
            // latitude =   0 =>    y = height/2
            // latitude = -90 =>    y = 0
            // and:
            // longitude = -180 =>    x = 3/4 * width
            // longitude =  -90 =>    x = 1/2 * width
            // longitude =    0 =>    x = 1/4 * width
            // longitude =  +90 =>    x = 0
            // longitude = +180 =>    x = 3/4 * width

            if (FindColorMap(body) is MapSO texture)
            {
                int x = Convert.ToInt32((90 - lon) / 360 * texture.Width);
                int y = Convert.ToInt32((90 + lat) / 180 * texture.Height);

                x = x > 0 ? x : texture.Width + x;

                x = Mathf.Clamp(x, 0, texture.Width);
                y = Mathf.Clamp(y, 0, texture.Height);

                col = texture.GetPixelColor(x, y);
                Debug.Log($"KSCSwitcher parsed {col} from color map at {x}, {y}");
                col *= colorMult;
                return true;
            }

            return false;
        }

        public static MapSO FindColorMap(CelestialBody body)
        {
            Transform t;

            t = body?.pqsController?.transform?.Find("VertexColorMapBlend");
            var mod = t?.GetComponent<PQSMod_VertexColorMapBlend>();
            if (mod?.vertexColorMap is MapSO map)
                return map;

            // if VertexColorMapBlend is not there, try with VertexColorMap
            t = body.pqsController?.transform?.Find("VertexColorMap");
            var mod2 = t?.GetComponent<PQSMod_VertexColorMap>();
            if (mod2?.vertexColorMap is MapSO map2)
                return map2;

            var mods = Resources.FindObjectsOfTypeAll<PQSMod_VertexColorMapBlend>();
            return mods.FirstOrDefault(m => m.sphere.PQSModCBTransform.body == body)?.vertexColorMap;
        }

        private Color BlendedOriginal(Color color, float f = 0.2f)
        {
            return Color.Lerp(color, originalGrassColor, f);
        }
    }

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorGrassFixer : MonoBehaviour
    {
        public void Start()
        {
            GameObject scenery = GameObject.Find("VABscenery") ?? GameObject.Find("SPHscenery");
            Material material = scenery?.GetChild("ksc_terrain")?.GetComponent<Renderer>()?.sharedMaterial;

            if (material == null)
            {
                return;
            }

            material.color = GrassSeasoner.GroundColor * 1.5f;
        }
    }
}
