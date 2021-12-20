namespace Words
{
    class nGram
    {
        public int distance { get; set; }
        public string contents { get; set; }
        public int nGramLength { get; set; }
        public string targetWord { get; set; }
        public string orientation { get; set; }

        public nGram(int distance, string contents, int nGramLength, string targetWord, string orientation)
        {
            this.distance = distance;
            this.contents = contents;
            this.nGramLength = nGramLength;
            this.targetWord = targetWord;
            this.orientation = orientation;
        }
        public override bool Equals(object obj)
        {
            if (!(obj is nGram)) return false;
            nGram gram = (nGram)obj;
            return gram.distance == this.distance && gram.contents == this.contents && gram.targetWord == this.targetWord && gram.orientation == this.orientation;
        }

        public override int GetHashCode()
        {
            return this.targetWord.GetHashCode() + this.contents.GetHashCode() + this.orientation.GetHashCode() + this.distance.GetHashCode();
        }

        public override string ToString()
        {
            return this.targetWord + ":" + this.contents + ":" + this.orientation + ":" + this.distance;
        }
    }
}
