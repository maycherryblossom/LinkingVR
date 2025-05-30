using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public class TesseractWrapper
{
#if UNITY_EDITOR
    private const string TesseractDllName = "tesseract";
    private const string LeptonicaDllName = "tesseract";
#elif UNITY_ANDROID
    private const string TesseractDllName = "libtesseract.so";
    private const string LeptonicaDllName = "liblept.so";
#else
    private const string TesseractDllName = "tesseract";
    private const string LeptonicaDllName = "tesseract";
#endif

    private IntPtr _tessHandle;
    private Texture2D _highlightedTexture;
    private string _errorMsg;
    private const float MinimumConfidence = 60;
    
    // Store detected words and their bounding boxes
    private List<DetectedWord> _detectedWords = new List<DetectedWord>();
    
    // Set detected words from external source (used for caching)
    public void SetDetectedWords(List<DetectedWord> words)
    {
        _detectedWords.Clear();
        if (words != null)
        {
            foreach (var word in words)
            {
                _detectedWords.Add(word);
            }
        }
    }
    
    // Class to store detected word information
    public class DetectedWord
    {
        public string Text;
        public Rect BoundingBox;
        public float Confidence;
        
        public DetectedWord(string text, Rect boundingBox, float confidence)
        {
            Text = text;
            BoundingBox = boundingBox;
            Confidence = confidence;
        }
    }

    [DllImport(TesseractDllName)]
    private static extern IntPtr TessVersion();

    [DllImport(TesseractDllName)]
    private static extern IntPtr TessBaseAPICreate();

    [DllImport(TesseractDllName)]
    private static extern int TessBaseAPIInit3(IntPtr handle, string dataPath, string language);

    [DllImport(TesseractDllName)]
    private static extern void TessBaseAPIDelete(IntPtr handle);

    [DllImport(TesseractDllName)]
    private static extern void TessBaseAPISetImage(IntPtr handle, IntPtr imagedata, int width, int height,
        int bytes_per_pixel, int bytes_per_line);

    [DllImport(TesseractDllName)]
    private static extern void TessBaseAPISetImage2(IntPtr handle, IntPtr pix);

    [DllImport(TesseractDllName)]
    private static extern int TessBaseAPIRecognize(IntPtr handle, IntPtr monitor);

    [DllImport(TesseractDllName)]
    private static extern IntPtr TessBaseAPIGetUTF8Text(IntPtr handle);

    [DllImport(TesseractDllName)]
    private static extern void TessDeleteText(IntPtr text);

    [DllImport(TesseractDllName)]
    private static extern void TessBaseAPIEnd(IntPtr handle);

    [DllImport(TesseractDllName)]
    private static extern void TessBaseAPIClear(IntPtr handle);

    [DllImport(TesseractDllName)]
    private static extern IntPtr TessBaseAPIGetWords(IntPtr handle, IntPtr pixa);
    
    [DllImport(TesseractDllName)]
    private static extern IntPtr TessBaseAPIAllWordConfidences(IntPtr handle);

    public TesseractWrapper()
    {
        _tessHandle = IntPtr.Zero;
    }

    public string Version()
    {
        IntPtr strPtr = TessVersion();
        string tessVersion = Marshal.PtrToStringAnsi(strPtr);
        return tessVersion;
    }

    public string GetErrorMessage()
    {
        return _errorMsg;
    }

    public bool Init(string lang, string dataPath)
    {
        if (!_tessHandle.Equals(IntPtr.Zero))
            Close();

        try
        {
            _tessHandle = TessBaseAPICreate();
            if (_tessHandle.Equals(IntPtr.Zero))
            {
                _errorMsg = "TessAPICreate failed";
                return false;
            }

            if (string.IsNullOrWhiteSpace(dataPath))
            {
                _errorMsg = "Invalid DataPath";
                return false;
            }

            int init = TessBaseAPIInit3(_tessHandle, dataPath, lang);
            if (init != 0)
            {
                Close();
                _errorMsg = "TessAPIInit failed. Output: " + init;
                return false;
            }
        }
        catch (Exception ex)
        {
            _errorMsg = ex + " -- " + ex.Message;
            return false;
        }

        return true;
    }

    public string Recognize(Texture2D texture)
    {
        if (_tessHandle.Equals(IntPtr.Zero))
            return null;

        _highlightedTexture = texture;
        _detectedWords.Clear();

        int width = _highlightedTexture.width;
        int height = _highlightedTexture.height;
        Color32[] colors = _highlightedTexture.GetPixels32();
        int count = width * height;
        int bytesPerPixel = 4;
        byte[] dataBytes = new byte[count * bytesPerPixel];
        int bytePtr = 0;

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                int colorIdx = y * width + x;
                dataBytes[bytePtr++] = colors[colorIdx].r;
                dataBytes[bytePtr++] = colors[colorIdx].g;
                dataBytes[bytePtr++] = colors[colorIdx].b;
                dataBytes[bytePtr++] = colors[colorIdx].a;
            }
        }

        IntPtr imagePtr = Marshal.AllocHGlobal(count * bytesPerPixel);
        Marshal.Copy(dataBytes, 0, imagePtr, count * bytesPerPixel);

        TessBaseAPISetImage(_tessHandle, imagePtr, width, height, bytesPerPixel, width * bytesPerPixel);

        if (TessBaseAPIRecognize(_tessHandle, IntPtr.Zero) != 0)
        {
            Marshal.FreeHGlobal(imagePtr);
            return null;
        }
        
        IntPtr confidencesPointer = TessBaseAPIAllWordConfidences(_tessHandle);
        int i = 0;
        List<int> confidence = new List<int>();
        
        while (true)
        {
            int tempConfidence = Marshal.ReadInt32(confidencesPointer, i * 4);

            if (tempConfidence == -1) break;

            i++;
            confidence.Add(tempConfidence);
        }

        int pointerSize = Marshal.SizeOf(typeof(IntPtr));
        IntPtr intPtr = TessBaseAPIGetWords(_tessHandle, IntPtr.Zero);
        Boxa boxa = Marshal.PtrToStructure<Boxa>(intPtr);
        Box[] boxes = new Box[boxa.n];

        IntPtr stringPtr = TessBaseAPIGetUTF8Text(_tessHandle);
        if (stringPtr.Equals(IntPtr.Zero))
        {
            Marshal.FreeHGlobal(imagePtr);
            return null;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string recognizedText = Marshal.PtrToStringAnsi(stringPtr);
#else
        string recognizedText = Marshal.PtrToStringAuto(stringPtr);
#endif

        string[] words = recognizedText.Split(new[] {' ', '\n'}, StringSplitOptions.RemoveEmptyEntries);
        StringBuilder result = new StringBuilder();

        // Store word boxes and draw highlights
        int minLength = Math.Min(boxes.Length, Math.Min(words.Length, confidence.Count));
        for (int index = 0; index < minLength; index++)
        {
            if (confidence[index] >= MinimumConfidence)
            {
                IntPtr boxPtr = Marshal.ReadIntPtr(boxa.box, index * pointerSize);
                boxes[index] = Marshal.PtrToStructure<Box>(boxPtr);
                Box box = boxes[index];
                
                // Create rect with correct coordinates (y-axis is flipped in Unity)
                Rect boundingBox = new Rect(box.x, _highlightedTexture.height - box.y - box.h, box.w, box.h);
                
                // Store the detected word
                _detectedWords.Add(new DetectedWord(words[index], boundingBox, confidence[index]));
                
                // Store the bounding box but don't try to draw on the texture
                // We'll visualize the boxes using Unity UI or GameObjects instead
                
                // Add to result string
                result.Append(words[index]);
                result.Append(" ");
                
                // Debug.Log($"Word: {words[index]}, Confidence: {confidence[index]}, Box: {boundingBox}");
            }
        }

        TessBaseAPIClear(_tessHandle);
        TessDeleteText(stringPtr);
        Marshal.FreeHGlobal(imagePtr);

        return result.ToString();
    }

    // This method is kept for compatibility but no longer draws on the texture
    // We'll visualize the bounding boxes using Unity GameObjects instead
    private void DrawLines(Texture2D texture, Rect boundingRect, Color color, int thickness = 3)
    {
        // We're not drawing on the texture anymore to avoid format compatibility issues
        // The visualization is handled by the KeywordDetector using 3D objects
    }

    public Texture2D GetHighlightedTexture()
    {
        return _highlightedTexture;
    }
    
    // Get all detected words with their bounding boxes
    public List<DetectedWord> GetDetectedWords()
    {
        return _detectedWords;
    }
    
    // Find all instances of a specific keyword
    public List<DetectedWord> FindKeyword(string keyword, bool caseSensitive = false)
    {
        List<DetectedWord> matches = new List<DetectedWord>();
        StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        Debug.Log($"FindKeyword: Searching for '{keyword}' among {_detectedWords.Count} words");
        
        // Print all detected words for debugging
        string allWords = "Detected words: ";
        foreach (var word in _detectedWords)
        {
            allWords += word.Text + ", ";
        }
        Debug.Log(allWords);
        
        foreach (var word in _detectedWords)
        {
            // Debug.Log($"Comparing '{word.Text}' with '{keyword}': {word.Text.Equals(keyword, comparison)}");
            if (word.Text.Equals(keyword, comparison))
            {
                Debug.Log($"MATCH FOUND: '{word.Text}' matches '{keyword}'");
                matches.Add(word);
            }
        }
        
        Debug.Log($"FindKeyword: Found {matches.Count} matches for '{keyword}'");
        return matches;
    }
    
    // Find all instances of keywords that contain the search term
    public List<DetectedWord> FindKeywordsContaining(string searchTerm, bool caseSensitive = false)
    {
        List<DetectedWord> matches = new List<DetectedWord>();
        
        if (string.IsNullOrWhiteSpace(searchTerm) || _detectedWords.Count == 0)
        {
            Debug.LogWarning($"Invalid search term or no detected words available. SearchTerm: '{searchTerm}', DetectedWords: {_detectedWords.Count}");
            return matches;
        }
        
        StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        // Clean up the search term (remove extra spaces, etc.)
        searchTerm = searchTerm.Trim();
        
        Debug.Log($"FindKeywordsContaining: Searching for '{searchTerm}' in {_detectedWords.Count} words");
        
        foreach (var word in _detectedWords)
        {
            if (word.Text.IndexOf(searchTerm, comparison) >= 0)
            {
                matches.Add(word);
                Debug.Log($"Found partial match: '{word.Text}' contains '{searchTerm}'");
            }
        }
        
        // If no exact matches were found, try more flexible matching
        if (matches.Count == 0)
        {
            Debug.Log("No exact partial matches found, trying more flexible matching...");
            
            // Try matching with reduced search term (e.g. if search term has spaces)
            string reducedSearchTerm = searchTerm.Replace(" ", "");
            if (reducedSearchTerm != searchTerm)
            {
                foreach (var word in _detectedWords)
                {
                    string reducedText = word.Text.Replace(" ", "");
                    if (reducedText.IndexOf(reducedSearchTerm, comparison) >= 0)
                    {
                        matches.Add(word);
                        Debug.Log($"Found flexible match: '{word.Text}' (reduced: '{reducedText}') contains '{searchTerm}' (reduced: '{reducedSearchTerm}')");
                    }
                }
            }
        }
        
        Debug.Log($"FindKeywordsContaining: Found {matches.Count} matches for '{searchTerm}'");
        return matches;
    }
    
    // Find a phrase (multiple words) in the recognized text
    public List<PhraseMatch> FindPhrase(string phrase, bool caseSensitive = false)
    {
        List<PhraseMatch> matches = new List<PhraseMatch>();
        if (string.IsNullOrEmpty(phrase) || _detectedWords.Count == 0)
            return matches;
            
        StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string[] searchWords = phrase.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (searchWords.Length == 0)
            return matches;
            
        // If it's a single word, use the simpler method
        if (searchWords.Length == 1)
        {
            foreach (var match in FindKeyword(searchWords[0], caseSensitive))
            {
                matches.Add(new PhraseMatch(new List<DetectedWord> { match }, match.BoundingBox));
            }
            return matches;
        }
        
        // For multi-word phrases, we need to find sequences of words
        for (int i = 0; i <= _detectedWords.Count - searchWords.Length; i++)
        {
            bool isMatch = true;
            List<DetectedWord> matchingWords = new List<DetectedWord>();
            
            for (int j = 0; j < searchWords.Length; j++)
            {
                if (i + j >= _detectedWords.Count || 
                    !_detectedWords[i + j].Text.Equals(searchWords[j], comparison))
                {
                    isMatch = false;
                    break;
                }
                matchingWords.Add(_detectedWords[i + j]);
            }
            
            if (isMatch && matchingWords.Count > 0)
            {
                // Calculate the combined bounding box for all words in the phrase
                float minX = matchingWords.Min(w => w.BoundingBox.x);
                float minY = matchingWords.Min(w => w.BoundingBox.y);
                float maxX = matchingWords.Max(w => w.BoundingBox.x + w.BoundingBox.width);
                float maxY = matchingWords.Max(w => w.BoundingBox.y + w.BoundingBox.height);
                
                Rect combinedBox = new Rect(minX, minY, maxX - minX, maxY - minY);
                matches.Add(new PhraseMatch(matchingWords, combinedBox));
            }
        }
        
        return matches;
    }
    
    // Class to represent a matched phrase (multiple words)
    public class PhraseMatch
    {
        public List<DetectedWord> Words { get; private set; }
        public Rect BoundingBox { get; private set; }
        
        public PhraseMatch(List<DetectedWord> words, Rect boundingBox)
        {
            Words = words;
            BoundingBox = boundingBox;
        }
        
        public string GetText()
        {
            return string.Join(" ", Words.Select(w => w.Text));
        }
    }

    public void Close()
    {
        if (_tessHandle.Equals(IntPtr.Zero))
            return;
        TessBaseAPIEnd(_tessHandle);
        TessBaseAPIDelete(_tessHandle);
        _tessHandle = IntPtr.Zero;
    }
}