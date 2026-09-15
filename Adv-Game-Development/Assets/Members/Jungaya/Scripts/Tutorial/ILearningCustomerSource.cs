using UnityEngine;
using Toufuku.Rescue;

namespace Toufuku.Tutorial
{
    /*
        段階学習（#58）が「決まった色の客を、決まった帯に1人ずつ置く」ための窓口

        客を出す側（今は MockCrowdDirector。本番のスポナーを作ったらそれ）が実装する
        段階学習はこの窓口だけを使うので、客の見た目（輪郭の作り方）や定位置の決め方は出す側にまかせられる
    */
    public interface ILearningCustomerSource
    {
        // ON の間はふつうの補充をしない（学習の客だけにする）。OFF にもどしたら時間割どおりに補充する
        bool AutoSpawnSuspended { get; set; }

        // 帯の数（0: 近 / 1: 中 / 2: 遠）
        int BandCount { get; }

        /*
            色 color の通常客を帯 bandIndex に1人置いて、その GameObject を返す。置けなければ null
            instant: true なら歩かせずに定位置にすぐ置く
        */
        GameObject SpawnLearningCustomer(OmamoriType color, int bandIndex, bool instant);

        // 同期パルス: 客の輪郭を amount01（0〜1）だけ明るくする。0 でもとにもどす
        void SetLearningPulse(GameObject customer, float amount01);
    }
}
