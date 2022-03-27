using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using ServicioLematizacion;

namespace Words
{
    public partial class Form1 : Form
    {
        // Aquí se debe especificar la ruta del directorio base.
        private ConcurrentDictionary<string, ConcurrentQueue<ParamtextFrase>> wordsParamtexts;
        private ConcurrentDictionary<string, ConcurrentQueue<ParamtextFrase>> wordsResolvedParamtexts;
        private ConcurrentBag<PhraseLinker> wordsLocations;
        private ConcurrentDictionary<string, ConcurrentQueue<string>> wordsFrasesGramaticales;
        private ConcurrentDictionary<string, ConcurrentQueue<string>> wordsFrasesCanonicas;
        private string baseDirectory;
        Regex wordRegex = new Regex(@"\p{L}+");
        public Form1()
        {
            InitializeComponent();
        }

        /***
         * 
         * Estructura de directorios:
         * En el directorio en el que se va a trabajar, deben existir tres cosas:
         * 2 directorios: "desambiguacion" y "phrases"
         * --> "desambiguacion": Directorio donde se guardan los n-gramas y las frases.
         * --> "phrases": Directorio donde se encuentran las frases con las que se desea tratar. Debe haber mínimo un fichero de frases.
         * 1 fichero: "Palabras ambiguas.txt": Debe contener las palabras a tratar.
         */
        private void Form1_Load(object sender, EventArgs e)
        {}
        /***
         * Aquí es donde se empieza a trabajar.
         */
        async private Task work ()
        {
            try
            {
                ServicioLematizacionClient servicioLematizacion = new();
                progressBar1.Minimum = 0;
                progressBar1.Value = 0;
                wordsParamtexts = new();
                wordsResolvedParamtexts = new();
                wordsLocations = new();
                wordsFrasesGramaticales = new();
                wordsFrasesCanonicas = new();
                label2.Text = "Cargando palabras...";
                label2.Refresh();
                StreamReader wordsFile = null;
                try
                {
                    wordsFile = new(baseDirectory + "palabras ambiguas.txt", true);
                } catch (Exception e)
                {
                    label2.Text = "Error: No se han podido cargar las palabas ambiguas. Verifique que el directorio es correcto.";
                    label2.Refresh();
                    return;
                }
                string[] words = wordsFile.ReadToEnd().Split("\r\n");
                wordsFile.Close();
                progressBar1.Maximum = words.Length;
                progressBar1.Refresh();
                await Task.Run(() => Parallel.ForEach(words, new ParallelOptions { MaxDegreeOfParallelism = 50 }, word =>
                {
                    wordsParamtexts.TryAdd(word, new());
                    wordsResolvedParamtexts.TryAdd(word, new());
                    if (!Directory.Exists(baseDirectory + @"desambiguación\" + word))
                    {
                        Directory.CreateDirectory(baseDirectory + @"desambiguación\" + word);
                    }
                    
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                }));
                progressBar1.Value = 0;
                progressBar1.Maximum = Directory.EnumerateFiles(baseDirectory + "phrases", "*.txt").Count();
                progressBar1.Refresh();
                label2.Text = "Cargando frases...";
                label2.Refresh();
                foreach (string file in Directory.EnumerateFiles(baseDirectory + "phrases", "*.txt"))
                {
                    StreamReader sr = new StreamReader(file);
                    string[] contents = sr.ReadToEnd().Split("\r\n");
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                    Parallel.ForEach(contents, new ParallelOptions { MaxDegreeOfParallelism = 50 }, phrase =>
                    {
                        CheckAmbiguousWordsInPhrase(phrase);
                    });
                    sr.Close();
                }
                progressBar1.Value = 0;
                progressBar1.Maximum = words.Length;
                progressBar1.Refresh();
                label2.Text = "Obteniendo formas canónicas y gramaticales de frases...";
                label2.Refresh();
                // Las categorías las sacamos aquí para no hacer tantas llamadas a la API.
                Dictionary<int, InfoCategoria> categorias = servicioLematizacion.ConsultaCodigosCategorias();

                // Aquí es donde se lanza la obtención de frases por forma canónica y gramatical.
                await Task.Run(() => Parallel.ForEach(wordsParamtexts.Keys, new ParallelOptions {MaxDegreeOfParallelism = 2}, word =>
                {
                    ObtenerFormaCanonicaGramatical(word, categorias);
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                }));
                wordsParamtexts = null; // no se usará más, liberamos memoria
                label2.Text = "Escribiendo frases en ficheros...";
                Console.WriteLine(wordsLocations.Where(x => (x.location >= x.fraseCanonica.Split(" ").Length)).ToList());
                label2.Refresh();
                progressBar1.Value = 0;
                progressBar1.Maximum = wordsResolvedParamtexts.Count*3;
                progressBar1.Refresh();
                await Task.Run(() => Parallel.ForEach(words, new ParallelOptions { MaxDegreeOfParallelism = 50 }, word =>
                {
                    if (!wordsResolvedParamtexts[word].IsEmpty) WriteWordsPhrasesToFile(word, categorias);
                    this.Invoke(new Action(() => progressBar1.Increment(1)));
                }));
                progressBar1.Value = 0;
                progressBar1.Maximum = wordsResolvedParamtexts.Count;
                label2.Text = "Desambiguando...";
                label2.Refresh();
                progressBar1.Refresh();
                await Task.Run(() => Parallel.ForEach(words, new ParallelOptions { MaxDegreeOfParallelism = 5 }, word =>
                {
                    ConcurrentDictionary<nGram, int> nGramCounts = new();
                    ConcurrentDictionary<nGram, int> nGramCountsCanonicas = new();
                    ConcurrentDictionary<nGram, int> nGramCountsGramaticales = new();
                    if (wordsResolvedParamtexts[word].IsEmpty) return;
                    Parallel.ForEach(wordsResolvedParamtexts[word], new ParallelOptions { MaxDegreeOfParallelism = 10 }, phrase =>
                    {
                        Disambiguate(nGramCounts, phrase.Frase, word);
                    });
                    Parallel.ForEach(wordsLocations.Where(x => x.targetWord == word), new ParallelOptions { MaxDegreeOfParallelism = 10 }, linker =>
                    {
                        DisambiguateAtLocation(nGramCountsCanonicas, linker.fraseCanonica, linker.location, false);
                        DisambiguateAtLocation(nGramCountsGramaticales, linker.fraseGramatical, linker.location, true);
                    });
                    foreach (nGram result in nGramCounts.Keys)
                    {
                        try
                        {
                            StreamWriter sw = new StreamWriter(baseDirectory + @"desambiguación\" + word + @"\" + word + "_" + result.orientation + "_" + result.nGramLength + ".txt", append: true);
                            sw.WriteLine(result.contents + ":" + result.distance + ":" + nGramCounts[result]);
                            sw.Flush();
                            sw.Close();
                        }
                        catch (Exception ex)
                        {
                            this.Invoke(new Action(() => label2.Text = "Ha habido un problema al procesar el n-grama: " + result));
                            return;
                        }
                    }
                    foreach (nGram result in nGramCountsCanonicas.Keys)
                    {
                        try
                        {
                            StreamWriter sw = new StreamWriter(baseDirectory + @"desambiguación\" + word + @"\" + word + "_" + result.orientation + "_" + result.nGramLength + "_canonicas.txt", append: true);
                            sw.WriteLine(result.contents + ":" + result.distance + ":" + nGramCountsCanonicas[result]);
                            sw.Flush();
                            sw.Close();
                        }
                        catch (Exception ex)
                        {
                            this.Invoke(new Action(() => label2.Text = "Ha habido un problema al procesar el n-grama: " + result));
                            return;
                        }
                    }
                    foreach (nGram result in nGramCountsGramaticales.Keys)
                    {
                        try
                        {
                            StreamWriter sw = new StreamWriter(baseDirectory + @"desambiguación\" + word + @"\" + word + "_" + result.orientation + "_" + result.nGramLength + "_categorias_gramaticales.txt", append: true);
                            sw.WriteLine(result.contents + ":" + result.distance + ":" + nGramCountsGramaticales[result]);
                            sw.Flush();
                            sw.Close();
                        }
                        catch (Exception ex)
                        {
                            this.Invoke(new Action(() => label2.Text = "Ha habido un problema al procesar el n-grama: " + result));
                            return;
                        }
                    }
                    this.Invoke(new Action(() => { progressBar1.Increment(1); label3.Text = "Última palabra desambiguada: " + word; }));
                }));
                label3.Text = "";
                label2.Text = "Todas las palabras y frases se han procesado.";
            }
            catch (Exception ex)
            {
                label2.Text = ex.Message;
            }
        }
        // esto es simplemente un enumerable que permite ajustar el tamaño del incremento para ser usado en un Parallel.forEach
        static class BetterEnumerable
        {
            public static IEnumerable<int> SteppedRange(int fromInclusive, int toExclusive, int step)
            {
                for (var i = fromInclusive; i < toExclusive; i += step)
                {
                    yield return i;
                }
            }
        }

        private void ObtenerFormaCanonicaGramatical(string word, Dictionary<int, InfoCategoria> categorias)
        {
            // Ya que obtener muchas frases de una vez tarda mucho tiempo,
            // he optado por obtenerlas de 50 en 50, en 10 hilos diferentes
            Parallel.ForEach(BetterEnumerable.SteppedRange(0, wordsParamtexts[word].Count, 50), new ParallelOptions {MaxDegreeOfParallelism = 25}, (index) =>
            {
                ServicioLematizacionClient servicio = new();
                List<ParamtextFrase> phraseCollection = wordsParamtexts[word].Skip(index).Take(50).ToList();
                List<ParamtextFrase> resolvedList = servicio.ReconocerLinguakit(phraseCollection, "es",false);
                foreach (ParamtextFrase frase in resolvedList)
                {
                    wordsResolvedParamtexts[word].Enqueue(frase);
                }
            });
            foreach (ParamtextFrase frase in wordsResolvedParamtexts[word])
            {
                string fraseLemas = "";
                string fraseGramaticales = "";
                foreach (ParamtextPalabra pal in frase.Palabras)
                {
                    if (pal.InformacionMorfologica != null)
                    {
                        fraseLemas += pal.InformacionMorfologica[0].InfoCanonica.FormaCanonica + " ";
                        fraseGramaticales += categorias[pal.InformacionMorfologica[0].InfoCanonica.IdCategoria].CategoriaAbrevEs + "#";
                    }
                    else
                    {
                        fraseLemas += pal.Palabra + " ";
                        fraseGramaticales += "desc." + "#";
                    }
                }
                PhraseLinker linker = wordsLocations.Where(x => x.fraseNormal == frase.Frase && x.wasProcessed == false).First();
                linker.wasProcessed = true;
                if (!wordsFrasesCanonicas.ContainsKey(word))
                {
                    wordsFrasesCanonicas.TryAdd(word, new ConcurrentQueue<string>());
                }
                wordsFrasesCanonicas[word].Enqueue(fraseLemas.Trim());
                linker.fraseCanonica = fraseLemas.Trim();
                if (!wordsFrasesGramaticales.ContainsKey(word))
                {
                    wordsFrasesGramaticales.TryAdd(word, new ConcurrentQueue<string>());
                }
                wordsFrasesGramaticales[word].Enqueue(fraseGramaticales[0..^1]);
                linker.fraseGramatical = fraseGramaticales[0..^1];
            }
        }
        private void Disambiguate(ConcurrentDictionary<nGram, int> nGramCounts, string phrase, string targetWord, int occurrence = 1, bool recursiveDisambiguate = true)
        {
            List<string> nGramsLeft = new();
            List<string> nGramsRight = new();
            string[] phraseParts = phrase.Split(" ");
            if (phraseParts.Length == 1)
            {
                string tempTargetWord = targetWord.Substring(0, 1).ToUpper() + targetWord.Substring(1);
                phraseParts = phrase.Split(tempTargetWord);
            }
            // Add words to nGrams.
            int qtyTargetWords = Regex.Matches(phrase, @"\b(?i)" + targetWord + @"\b").Count;
            if (qtyTargetWords > 1 && occurrence < qtyTargetWords && recursiveDisambiguate)
            {
                Disambiguate(nGramCounts, phrase, targetWord, occurrence+1);
            }
            bool flagFound = false;
            foreach (string word in phraseParts)
            {
                if (wordRegex.Match(word.ToLower()).ToString() == targetWord.ToLower() && occurrence > 0)
                {
                    occurrence--;
                    if (occurrence == 0)
                    {
                        flagFound = true;
                        continue;
                    }                    
                }
                if (!flagFound)
                {
                    nGramsLeft.Add(word);
                } else
                {
                    nGramsRight.Add(word);
                }
            }
            int distance = 3;
            for (int i = 0; i < distance; i++)
            {
                string[] dividedPhrase = nGramsLeft.ToArray();
                int nGram = 1;
                while (nGram <= 4)
                {
                    nGram result = ProcessNGram(GetNGramAtDistance(i + 1, nGram, dividedPhrase, "left"), targetWord, "left", nGram, i + 1, nGramCounts);
                    if (result.contents == "") break;
                    nGram++;
                }
                dividedPhrase = nGramsRight.ToArray();
                nGram = 1;
                while (nGram <= 4)
                {
                    nGram result = ProcessNGram(GetNGramAtDistance(i + 1, nGram, dividedPhrase, "right"), targetWord, "right", nGram, i + 1, nGramCounts);
                    if (result.contents == "") break;
                    nGram++;
                }
            }
            return;
        }
        private void DisambiguateAtLocation(ConcurrentDictionary<nGram, int> nGramCounts, string phrase, int location, bool isFraseGramaticales)
        {
            List<string> nGramsLeft = new();
            List<string> nGramsRight = new();
            string[] phraseWords;
            if (!isFraseGramaticales) phraseWords = phrase.Split(" ");
            else phraseWords = phrase.Split("#");
            for (int i = location-1; i >= 0; i--)
            {
                if (i == location - 3)
                {
                    break;
                }
                nGramsLeft.Add(phraseWords[i]);
            }
            for (int i = location+1; i < phraseWords.Length; i++)
            {
                if (i == location + 4)
                {
                    break;
                }
                nGramsRight.Add(phraseWords[i]);
            }
            int distance = 3;
            for (int i = 0; i < distance; i++)
            {
                string[] dividedPhrase = nGramsLeft.ToArray();
                int nGram = 1;
                while (nGram <= 4)
                {
                    nGram result = ProcessNGram(GetNGramAtDistance(i + 1, nGram, dividedPhrase, "left"), phraseWords[location], "left", nGram, i + 1, nGramCounts);
                    if (result.contents == "") break;
                    nGram++;
                }
                dividedPhrase = nGramsRight.ToArray();
                nGram = 1;
                while (nGram <= 4)
                {
                    nGram result = ProcessNGram(GetNGramAtDistance(i + 1, nGram, dividedPhrase, "right"), phraseWords[location], "right", nGram, i + 1, nGramCounts);
                    if (result.contents == "") break;
                    nGram++;
                }
            }
            return;
        }
        private string GetNGramAtDistance(int distance, int nGram, string[] dividedPhrase, string orientation)
        {
            string result = "";
            int count = 0;
            if (orientation == "left")
            {
                for (int i = dividedPhrase.Length - nGram - distance + 1; i < dividedPhrase.Length - distance + 1; i++)
                {
                    if (i < 0 || count == nGram) break;
                    result += " " + dividedPhrase[i];
                    count++;
                }
            }
            else
            {
                bool flag = false;
                for (int i = distance - 1; i < dividedPhrase.Length; i++)
                {
                    if (i >= dividedPhrase.Length || count == nGram) break;
                    if (dividedPhrase.Length - i >= nGram || flag)
                    {
                        flag = true;
                    }
                    else
                    {
                        break;
                    }
                    if (flag)
                    {
                        result += " " + dividedPhrase[i];
                        count++;
                    }
                }
            }
            return result.Trim();
        }
        private nGram ProcessNGram(string processedGram, string targetWord, string orientation, int nGram, int distance, ConcurrentDictionary<nGram, int> nGramCounts)
        {
            bool resultExists = false;
            nGram ngram = new nGram(distance, processedGram.Trim(), nGram, targetWord.ToLower(), orientation == "left" ? "izq" : "der");
            if (nGramCounts.ContainsKey(ngram))
            {
                nGramCounts[ngram]++;
            }
            if (!resultExists && ngram.contents != "")
            {
                nGramCounts.TryAdd(ngram, 1);
            }
            return ngram;
        }
        private void CheckAmbiguousWordsInPhrase(string phrase)
        {
            string fraseNormalizada = NormalizarFrase(phrase);
            string[] phraseWords = fraseNormalizada.Split(" ");
            for (int i = 0; i < phraseWords.Length; i++)
            {
                phraseWords[i] = wordRegex.Match(phraseWords[i]).ToString().ToLower();
            }
            for (int i = 0; i < phraseWords.Length; i++)
            {
                if (wordsParamtexts.ContainsKey(phraseWords[i]))
                {
                    ParamtextFrase str = CreateParamtextFrase(fraseNormalizada, phraseWords.Select(x => x.ToString().Trim()).Where(x => x != "").ToList());
                    if (!wordsParamtexts[phraseWords[i]].Contains(str))
                    {
                        wordsParamtexts[phraseWords[i]].Enqueue(str);
                    }
                    wordsLocations.Add(new PhraseLinker(fraseNormalizada, i, phraseWords[i]));
                }
            }
            return;
        }

        private string NormalizarFrase(string frase)
        {
            frase = frase.Replace("¿ ", "¿")
                .Replace(" ?", "?")
                .Replace(" ,", ",")
                .Replace(" .", ".")
                .Replace(" :", ":")
                .Replace(" ;", ";");
            return frase;
        }

        private void WriteWordsPhrasesToFile(string word, Dictionary<int, InfoCategoria> categorias)
        {
            StreamWriter streamWriter = null;
            try
            {
                streamWriter = new StreamWriter(baseDirectory + @"desambiguación\" + word + @"\frases.txt", append: true);
                StreamWriter streamWriterLemas = new StreamWriter(baseDirectory + @"desambiguación\" + word + @"\frases_lemas.txt", append: true);
                StreamWriter streamWriterGramaticales = new StreamWriter(baseDirectory + @"desambiguación\" + word + @"\frases_gramaticales.txt", append: true);
                foreach (ParamtextFrase frase in wordsResolvedParamtexts[word].ToList())
                {
                    streamWriter.WriteLine(frase.Frase);
                }
                foreach (string fraseLemas in wordsFrasesCanonicas[word])
                {
                    streamWriterLemas.WriteLine(fraseLemas.Trim());
                }
                foreach (string fraseGramaticales in wordsFrasesGramaticales[word])
                {
                    streamWriterGramaticales.WriteLine(fraseGramaticales.Trim());
                }
                streamWriter.Flush();
                streamWriter.Close();
                streamWriterLemas.Flush();
                streamWriterLemas.Close();
                streamWriterGramaticales.Flush();
                streamWriterGramaticales.Close();
            }
            catch (Exception e)
            {
            }
            finally
            {
                if (streamWriter != null) streamWriter.Close();
            }
            return;
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            DialogResult result = folderBrowserDialog1.ShowDialog();
            if (result == DialogResult.OK)
            {
                baseDirectory = folderBrowserDialog1.SelectedPath + @"\";
                textBox1.Text = baseDirectory;
                button1.Enabled = true;
            }
        }

        private async void Button1_Click(object sender, EventArgs e)
        {
            await work();
        }
        private ParamtextFrase CreateParamtextFrase(string sentenceText, List<string> palabras)
        {
            ParamtextFrase frase = new ParamtextFrase();
            frase.Frase = sentenceText;
            frase.Palabras = palabras.Select(x => CreateParamtextPalabra(x)).ToList();
            return frase;
        }

        private ParamtextPalabra CreateParamtextPalabra(string palabra)
        {
            string matchedPalabra = wordRegex.Match(palabra.ToLower()).ToString().ToLower();
            ParamtextPalabra ParamtextPalabra = new ParamtextPalabra();
            ParamtextPalabra.Palabra = matchedPalabra;
            return ParamtextPalabra;
        }
    }
}
