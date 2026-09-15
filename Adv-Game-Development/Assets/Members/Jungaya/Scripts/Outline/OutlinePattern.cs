namespace Toufuku.Rescue.Outline
{
    /*
        アウトラインの線の種類。色がわかりにくい人向けに「色＋もよう」で出し分けるために、先に用意しておいたもの

        今は Solid だけ作ってある。Dashed と Wavy は Compose シェーダーの
        ApplyPattern に入り口を作ってあるだけで、中身はまだ（色覚対応のとき用）

        マスクRT の A チャンネルにのせるときは「あるかないかのフラグ」といっしょに入れたいので、
        シェーダーで A = (1 + patternId) / 255 にして入れる
        （0 = マスクなし、1 = Solid、2 = Dashed、3 = Wavy）
        enum の数字（0/1/2）自体は変えない。+1 するのはシェーダーにのせるときだけの話
    */
    public enum OutlinePattern
    {
        // 実線（ふつうはこれ）
        Solid = 0,
        // 点線。まだ作っていない
        Dashed = 1,
        // 波線。まだ作っていない
        Wavy = 2,
    }
}
